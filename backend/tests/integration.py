"""Real HTTP + MySQL checks. Requires a fresh, dedicated xiebox_test_* database."""
import json
import os
import pathlib
import secrets
import hashlib
import http.server
import threading
import zipfile
import socket
import subprocess
import time
import urllib.error
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[2]
connection = os.environ.get("XIEBOX_TEST_MYSQL_CONNECTION")
if not connection:
    raise SystemExit("Set XIEBOX_TEST_MYSQL_CONNECTION to a fresh empty xiebox_test_* database. See backend/Database/README.md.")
DATA = ROOT / "artifacts" / "api-tests" / secrets.token_hex(6)
DATA.mkdir(parents=True)
DLL = ROOT / "backend/Api/bin/Debug/net10.0/XieBox.Api.dll"
password = "Test-" + secrets.token_hex(12)
admin_username = "test-administrator"
with socket.socket() as s:
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
base = f"http://127.0.0.1:{port}"
env = os.environ | {
    "ASPNETCORE_ENVIRONMENT": "Development",
    "ASPNETCORE_URLS": base,
    "Platform__DataDirectory": str(DATA),
    "XIEBOX_ADMIN_USERNAME": admin_username,
    "XIEBOX_ADMIN_PASSWORD": password,
    "Logging__LogLevel__Default": "Warning",
    "ConnectionStrings__PlatformDatabase": connection,
    "Mail__Mode": "DevelopmentFile",
}
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
checks = 0

def request(path, body=None, token=None, expected=200):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    req = urllib.request.Request(base + path, data=None if body is None else json.dumps(body).encode(), headers=headers)
    deadline = time.monotonic() + 70
    waiting = False
    while True:
        try:
            with opener.open(req, timeout=8) as response:
                status, data = response.status, response.read()
        except urllib.error.HTTPError as error:
            status, data = error.code, error.read()
        if status != 429 or expected == 429 or time.monotonic() >= deadline:
            break
        if not waiting:
            print("Waiting for the real auth rate-limit window to reset...", flush=True)
            waiting = True
        time.sleep(1)
    assert status == expected, f"{path}: expected {expected}, got {status}: {data[:300]!r}"
    return json.loads(data) if data else None

def check(condition, name):
    global checks
    assert condition, name
    checks += 1
    print("PASS:", name, flush=True)

def mail_code(challenge):
    return json.loads((DATA / "dev-mail" / (challenge["challengeId"] + ".json")).read_text())["code"]

def register(email, secret):
    challenge = request("/api/v1/auth/register", {"email": email, "password": secret})
    return request("/api/v1/auth/verify", {"challengeId": challenge["challengeId"], "code": mail_code(challenge)})

with (DATA / "bootstrap.log").open("w", encoding="utf-8") as log:
    result = subprocess.run(["dotnet", str(DLL), "--check-test-database"], cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=30)
    assert result.returncode == 0, "Test database must be empty and named xiebox_test_*; see isolated bootstrap.log"
    result = subprocess.run(["dotnet", str(DLL), "--bootstrap-admin"], cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=30)
assert result.returncode == 0, "Admin bootstrap failed; see isolated bootstrap.log"
log = (DATA / "server.log").open("w", encoding="utf-8")
process = subprocess.Popen(["dotnet", str(DLL)], cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT)
try:
    for _ in range(60):
        try:
            request("/health")
            break
        except (OSError, AssertionError):
            if process.poll() is not None:
                raise AssertionError("API exited; see isolated server.log")
            time.sleep(.15)
    else:
        raise AssertionError("API did not become ready")
    check(request("/health")["status"] == "ok", "API started with MySQL database and migrations")
    expiry = request("/api/v1/auth/register", {"email": "expiry@example.test", "password": password})
    expiry_deadline = time.monotonic() + 301
    catalog = request("/api/v1/modules")
    expected_catalog = json.loads((ROOT / "Resources/catalog.json").read_text(encoding="utf-8-sig"))
    check({m["id"] for m in catalog} == {m["id"] for m in expected_catalog}, "public module catalog available without login")
    request("/api/v1/modules/garbage-cleaner/capabilities/unknown", expected=404)
    check(True, "unknown module capability rejected")
    request("/api/v1/me", expected=401)
    request("/api/v1/admin/users", expected=401)
    check(True, "protected endpoints reject anonymous requests")
    admin = request("/api/v1/auth/admin-login", {"username": admin_username, "password": password})
    usernames = ["first-user@example.test", "second-user@example.test"]
    for username in usernames:
        pending = request("/api/v1/auth/register", {"email": username, "password": password})
        request("/api/v1/auth/login", {"username": username, "password": password}, expected=401)
        code = mail_code(pending)
        wrong = "000000" if code != "000000" else "111111"
        request("/api/v1/auth/verify", {"challengeId": pending["challengeId"], "code": wrong}, expected=400)
        request("/api/v1/auth/register", {"email": username, "password": password}, expected=429)
        started = request("/api/v1/auth/verify", {"challengeId": pending["challengeId"], "code": code})
        check(started["step"] == "signed_in" and started["session"]["user"]["username"] == username
              and started["session"]["user"]["email"] == username,
              "email registration requires a correct code and enforces resend cooldown")
        request("/api/v1/auth/verify", {"challengeId": pending["challengeId"], "code": code}, expected=400)
        check(True, "registration code cannot be replayed")
    check(len(request("/api/v1/admin/users", token=admin["accessToken"])) == 3,
          "verified email registration creates exactly one account per email")
    request("/api/v1/auth/continue", {"username": usernames[0], "password": "Wrong-password-for-existing"}, expected=401)
    check(len(request("/api/v1/admin/users", token=admin["accessToken"])) == 3,
          "wrong password on an existing username does not create a new account")
    request("/api/v1/auth/register", {"username": usernames[0], "password": password}, expected=400)
    check(True, "duplicate username registration rejected")
    for username, invalid_password in [("too-short", "12345"), ("too-long", "x" * 129), ("invalid name", "123456"), ("x" * 255, "123456")]:
        request("/api/v1/auth/continue", {"username": username, "password": invalid_password}, expected=400)
    check(len(request("/api/v1/admin/users", token=admin["accessToken"])) == 3,
          "invalid usernames and password lengths create no accounts")
    request("/api/v1/auth/continue", {"username": "unregistered-user", "password": password}, expected=400)
    unified = request("/api/v1/auth/continue", {"username": "unified@example.test", "password": password})
    check(unified["step"] == "verify_email" and unified.get("session") is None, "unified entry never signs in an unverified email")
    verified_unified = request("/api/v1/auth/verify", {"challengeId": unified["challenge"]["challengeId"], "code": mail_code(unified["challenge"])})
    request("/api/v1/auth/continue", {"username": "unified@example.test", "password": "wrong-password"}, expected=401)
    signed_unified = request("/api/v1/auth/continue", {"username": "unified@example.test", "password": password})
    check(signed_unified["step"] == "signed_in" and signed_unified["session"]["user"]["userId"] == verified_unified["session"]["user"]["userId"], "unified entry logs existing emails in and rejects wrong passwords")
    request("/api/v1/auth/register", {"email": "not-an-email", "password": password}, expected=400)
    request("/api/v1/auth/register", {"email": usernames[0].upper(), "password": password}, expected=400)
    check(True, "legacy unified endpoint cannot bypass email verification and duplicate emails are case insensitive")
    six = register("six@example.test", "123456")
    check(six["session"]["user"]["email"] == "six@example.test", "email registration accepts six-character passwords")
    blocked = request("/api/v1/auth/register", {"email": "blocked@example.test", "password": password})
    blocked_code = mail_code(blocked)
    wrong = "000000" if blocked_code != "000000" else "111111"
    for _ in range(5):
        request("/api/v1/auth/verify", {"challengeId": blocked["challengeId"], "code": wrong}, expected=400)
    request("/api/v1/auth/verify", {"challengeId": blocked["challengeId"], "code": blocked_code})
    check(True, "five incorrect registration codes do not invalidate the correct code")
    registration_mail = json.loads((DATA / "dev-mail" / (blocked["challengeId"] + ".json")).read_text())
    check(registration_mail["subject"] == "【蟹小箱】" + blocked_code
          and registration_mail["text"].startswith("注册账号验证码：" + blocked_code)
          and "5 分钟" in registration_mail["text"], "registration mail puts brand and code first in subject and body")
    unknown = request("/api/v1/auth/forgot-password", {"email": "unknown@example.test"})
    check(not (DATA / "dev-mail" / (unknown["challengeId"] + ".json")).exists(), "unknown email recovery returns generic response without sending mail")
    continued = request("/api/v1/auth/continue", {"username": usernames[0].upper(), "password": password})
    check(continued["step"] == "signed_in", "existing username signs in through unified entry regardless of casing")
    first = continued["session"]
    second = request("/api/v1/auth/login", {"username": usernames[1], "password": password})
    check(first["user"]["userId"] != second["user"]["userId"], "accounts have distinct platform user IDs")
    check(len(first["user"]["publicId"]) == 8 and first["user"]["publicId"].isascii()
          and first["user"]["publicId"].isalnum() and first["user"]["publicId"] != second["user"]["publicId"],
          "public UIDs are distinct eight-character letters and digits")
    reset = request("/api/v1/auth/forgot-password", {"email": "six@example.test"})
    reset_code = mail_code(reset)
    reset_mail = json.loads((DATA / "dev-mail" / (reset["challengeId"] + ".json")).read_text())
    check(reset_mail["subject"] == "【蟹小箱】" + reset_code
          and reset_mail["text"].startswith("找回密码验证码：" + reset_code)
          and "5 分钟" in reset_mail["text"], "recovery mail puts brand and code first in subject and body")
    request("/api/v1/auth/verify", {"challengeId": reset["challengeId"], "code": reset_code}, expected=400)
    for _ in range(5):
        wrong = "000000" if reset_code != "000000" else "111111"
        request("/api/v1/auth/reset-password", {"challengeId": reset["challengeId"], "code": wrong, "newPassword": password}, expected=400)
    request("/api/v1/auth/reset-password", {"challengeId": reset["challengeId"], "code": reset_code, "newPassword": password})
    request("/api/v1/me", token=six["session"]["accessToken"], expected=401)
    request("/api/v1/auth/refresh", {"refreshToken": six["session"]["refreshToken"]}, expected=401)
    request("/api/v1/auth/login", {"username": "six@example.test", "password": "123456"}, expected=401)
    request("/api/v1/auth/login", {"username": "SIX@example.test", "password": password})
    request("/api/v1/auth/reset-password", {"challengeId": reset["challengeId"], "code": reset_code, "newPassword": "another-password"}, expected=400)
    check(True, "password reset changes credentials, revokes access and refresh sessions, and consumes its purpose-bound code")
    check(request("/api/v1/me", token=first["accessToken"])["username"] == usernames[0], "current user belongs to session")
    request("/api/v1/admin/users", token=first["accessToken"], expected=403)
    check(True, "ordinary user cannot access admin API")
    refreshed = request("/api/v1/auth/refresh", {"refreshToken": first["refreshToken"]})
    check(refreshed["sessionExpiresAt"] == first["sessionExpiresAt"], "refresh cannot extend the seven-day session deadline")
    check(refreshed["user"]["publicId"] == first["user"]["publicId"], "refresh preserves the public UID")
    request("/api/v1/me", token=first["accessToken"], expected=401)
    check(request("/api/v1/me", token=refreshed["accessToken"])["username"] == usernames[0], "refresh rotates access and refresh credentials")
    request("/api/v1/auth/refresh", {"refreshToken": first["refreshToken"]}, expected=401)
    request("/api/v1/me", token=refreshed["accessToken"], expected=401)
    check(True, "refresh replay revokes rotated session")
    request("/api/v1/auth/logout", {}, second["accessToken"], expected=204)
    request("/api/v1/me", token=second["accessToken"], expected=401)
    check(True, "logout revokes server-side session immediately")
    admin_desktop = request("/api/v1/auth/login", {"username": admin_username, "password": password})
    request("/api/v1/admin/users", token=admin_desktop["accessToken"], expected=403)
    admin = request("/api/v1/auth/admin-login", {"username": admin_username, "password": password})
    check(len(request("/api/v1/admin/users", token=admin["accessToken"])) == 6, "admin API requires a separate admin session")
    fresh = request("/api/v1/auth/login", {"username": usernames[0], "password": password})
    request("/api/v1/admin/users/" + fresh["user"]["userId"] + "/state", {"disabled": True}, admin["accessToken"], expected=204)
    request("/api/v1/me", token=fresh["accessToken"], expected=401)
    check(True, "disabled user loses active session")
    request("/api/v1/admin/users/" + admin["user"]["userId"] + "/state", {"disabled": True}, admin["accessToken"], expected=400)
    check(True, "admin cannot disable own active account")
    request("/api/v1/admin/modules/missing-test-module/state", {"status": "available"}, admin["accessToken"], expected=404)
    package = DATA / "fixture.zip"
    with zipfile.ZipFile(package, "w") as archive:
        archive.writestr("module.json", json.dumps({"id": "test-content", "name": "安装测试", "version": "0.1.0", "runtime": "content", "entry": "page.json", "protocolVersion": 1}))
        archive.writestr("page.json", json.dumps({"title": "联网安装测试", "description": "这是受控测试模块。", "tips": ["测试完成后卸载"]}))
    class PackageHandler(http.server.BaseHTTPRequestHandler):
        def do_GET(self):
            if self.path != "/fixture.zip":
                self.send_error(404)
                return
            data = package.read_bytes()
            self.send_response(200)
            self.send_header("Content-Type", "application/zip")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)
        def log_message(self, *args):
            pass
    package_server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), PackageHandler)
    threading.Thread(target=package_server.serve_forever, daemon=True).start()
    release = {"id": "test-content", "name": "安装测试", "icon": "□", "description": "受控测试模块", "version": "0.1.0", "status": "available", "runtime": "content", "offline": True, "features": [], "capabilities": [{"id": "open", "policy": "free"}], "packageUrl": f"http://127.0.0.1:{package_server.server_port}/fixture.zip", "sha256": hashlib.sha256(package.read_bytes()).hexdigest(), "protocolVersion": 1}
    request("/api/v1/admin/modules", release, first["accessToken"], expected=401)
    request("/api/v1/admin/modules", release, admin["accessToken"])
    check(any(m["id"] == "test-content" for m in request("/api/v1/modules")), "admin publishes a module release into public catalog")
    check(request("/api/v1/modules/test-content/capabilities/open")["allowed"], "guest free capability allowed")
    request("/api/v1/admin/modules/test-content/state", {"status": "disabled"}, admin["accessToken"], expected=204)
    check(next(m for m in request("/api/v1/modules") if m["id"] == "test-content")["status"] == "disabled", "admin module changes persist")
    request("/api/v1/admin/modules/test-content/state", {"status": "available"}, admin["accessToken"], expected=204)
    check(len(request("/api/v1/admin/audit", token=admin["accessToken"])) >= 3, "admin operations audited")
    desktop_tests = ROOT / "tests/bin/Debug/net10.0-windows/XieBox.Platform.Tests.dll"
    if desktop_tests.exists():
        # Start a fresh limiter window for the desktop's independent auth checks.
        # Retain the actual configured rate limit rather than disabling it for tests.
        process.terminate()
        process.wait(timeout=8)
        process = subprocess.Popen(["dotnet", str(DLL)], cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT)
        for _ in range(60):
            try:
                request("/health")
                break
            except (OSError, AssertionError):
                if process.poll() is not None:
                    raise AssertionError("API exited before desktop checks; see isolated server.log")
                time.sleep(.15)
        else:
            raise AssertionError("API did not become ready for desktop checks")
        desktop_env = env | {"XIEBOX_TEST_API_URL": base + "/", "XIEBOX_TEST_USERNAME": usernames[1], "XIEBOX_TEST_PASSWORD": password, "XIEBOX_TEST_MODULE_ID": "test-content", "XIEBOX_TEST_MAIL_DIRECTORY": str(DATA / "dev-mail")}
        result = subprocess.run(["dotnet", str(desktop_tests), "--account-only"], cwd=ROOT, env=desktop_env, timeout=45)
        check(result.returncode == 0, "desktop account and module context integration")
    package_server.shutdown()
    package_server.server_close()
    smoke_env = env | {"XIEBOX_DATA_DIR": str(DATA / "desktop-smoke"), "XIEBOX_API_URL": base + "/"}
    smoke = subprocess.run(["dotnet", str(ROOT / "bin/Debug/net10.0-windows/XieBroToolBox.dll"), "--smoke-test"], cwd=ROOT, env=smoke_env, timeout=15)
    check(smoke.returncode == 0, "actual desktop application starts and renders its window")
    # Keep the real 20-auth-requests-per-minute policy enabled during integration checks.
    process.terminate()
    process.wait(timeout=8)
    process = subprocess.Popen(["dotnet", str(DLL)], cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT)
    for _ in range(60):
        try:
            request("/health")
            break
        except (OSError, AssertionError):
            if process.poll() is not None:
                raise AssertionError("Restarted API exited; see isolated server.log")
            time.sleep(.15)
    else:
        raise AssertionError("Restarted API did not become ready")
    check(any(m["id"] == "test-content" for m in request("/api/v1/modules"))
          and request("/api/v1/auth/login", {"username": usernames[1], "password": password})["user"]["username"] == usernames[1],
          "MySQL accounts and published modules persist across API restart")
    dev_env = env | {"XIEBOX_ADMIN_PASSWORD": "admin123"}
    with (DATA / "dev-bootstrap.log").open("w", encoding="utf-8") as dev_log:
        result = subprocess.run(["dotnet", str(DLL), "--bootstrap-dev-admin"], cwd=ROOT,
                                env=dev_env | {"ASPNETCORE_ENVIRONMENT": "Production"}, stdout=dev_log, stderr=subprocess.STDOUT, timeout=30)
        check(result.returncode != 0, "production refuses development account initialization")
        result = subprocess.run(["dotnet", str(DLL), "--bootstrap-dev-admin"], cwd=ROOT,
                                env=dev_env, stdout=dev_log, stderr=subprocess.STDOUT, timeout=30)
        check(result.returncode == 0, "operator creates development administrator with short test password")
        request("/api/v1/auth/continue", {"username": "admin", "password": "wrong-password"}, expected=401)
        check(True, "development account still checks its stored password")
        development = request("/api/v1/auth/continue", {"username": "admin", "password": "admin123"})
        check(development["step"] == "signed_in" and development["session"]["user"]["username"] == "admin",
              "development username signs in through unified desktop entry")
        request("/api/v1/admin/users", token=development["session"]["accessToken"], expected=403)
        management = request("/api/v1/auth/admin-login", {"username": "admin", "password": "admin123"})
        check(any(u["id"] == management["user"]["userId"] for u in request("/api/v1/admin/users", token=management["accessToken"])),
              "development administrator retains separate management session")
        result = subprocess.run(["dotnet", str(DLL), "--bootstrap-dev-admin"], cwd=ROOT,
                                env=dev_env, stdout=dev_log, stderr=subprocess.STDOUT, timeout=30)
        repeated = request("/api/v1/auth/continue", {"username": "admin", "password": "admin123"})
        check(result.returncode == 0 and repeated["session"]["user"]["userId"] == development["session"]["user"]["userId"],
              "repeated development initialization preserves the same account")
    remaining = expiry_deadline - time.monotonic()
    if remaining > 0:
        print(f"Waiting {remaining:.0f}s to verify the real five-minute code expiry...", flush=True)
        time.sleep(remaining)
    request("/api/v1/auth/verify", {"challengeId": expiry["challengeId"], "code": mail_code(expiry)}, expected=400)
    check(True, "correct registration code is rejected after five minutes, including across API restarts")
    print(f"PASS: {checks} API integration checks. Data: {DATA.relative_to(ROOT)}")
finally:
    process.terminate()
    try:
        process.wait(timeout=8)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait(timeout=5)
    log.close()
