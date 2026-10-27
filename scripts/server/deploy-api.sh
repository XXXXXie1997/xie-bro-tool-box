#!/usr/bin/env bash
# First installation only. Run from an uploaded bundle containing api/ and files.sha256.
set -Eeuo pipefail
umask 077
stage=$(cd -- "$(dirname -- "$0")" && pwd)
stamp=$(date -u +%Y%m%dT%H%M%SZ)
backup=/var/backups/xiebox-api/$stamp
config=/etc/nginx/conf.d/personal-projects.conf
release=/opt/xiebox/releases/$stamp
config_changed=0
service_created=0
mkdir -p "$backup"
rollback() {
    rc=$?; trap - ERR
    if [ "$config_changed" = 1 ]; then cp -a "$backup/personal-projects.conf" "$config"; nginx -t && systemctl reload nginx; fi
    if [ "$service_created" = 1 ]; then systemctl disable --now xiebox-api.service || true; fi
    echo "Installation stopped. Existing site configuration restored; isolated database and files retained. Backup: $backup"
    exit "$rc"
}
trap rollback ERR
test ! -e /opt/xiebox/current
test ! -f /etc/systemd/system/xiebox-api.service
test -f /etc/xiebox/mail.json
test -f /etc/mysql/debian.cnf
test -f "$config"
nginx -t
if ss -lnt | grep -q ':5188 '; then echo 'Port 5188 already occupied'; exit 1; fi
cp -a "$config" "$backup/personal-projects.conf"
free -m > "$backup/memory-before.txt"
(cd "$stage/api" && sha256sum -c ../files.sha256 > "$backup/package-check.txt")
export XIEBOX_INSTALL_STAGE="$stage" XIEBOX_INSTALL_BACKUP="$backup"
python3 - <<'PY'
import os,pathlib,subprocess,json,hashlib
text=pathlib.Path('/etc/nginx/conf.d/personal-projects.conf').read_text()
anchor='    # XieBro ToolBox official website.'
assert text.count(anchor)==1 and '/xie-bro-tool-box/api/' not in text, 'Nginx routing has changed'
block='''    # XieBro ToolBox account API, served by a loopback-only process.
    location ^~ /xie-bro-tool-box/api/ {
        if ($scheme = http) { return 400; }
        proxy_pass http://127.0.0.1:5188/api/;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_connect_timeout 5s;
        proxy_read_timeout 30s;
        client_max_body_size 64k;
    }

    location = /xie-bro-tool-box/health {
        proxy_pass http://127.0.0.1:5188/health;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-Proto $scheme;
        add_header Cache-Control "no-store" always;
    }

'''
pathlib.Path(os.environ['XIEBOX_INSTALL_STAGE'],'nginx.new').write_text(text.replace(anchor,block+anchor))
baseline={}
for route in ['/','/drawer/','/money-tracker/','/mo-ui/','/guide-demo/','/canvas-drawing-board-vite/','/xie-bro-tool-box/','/checkin-app']:
    raw=subprocess.check_output(['curl','-sS','--max-time','20','-H','Host: www.xie-bro.top','-w','\n%{http_code}','http://127.0.0.1'+route])
    body,status=raw.rsplit(b'\n',1)
    baseline[route]={'status':status.decode(),'hash':hashlib.sha256(body).hexdigest()}
pathlib.Path(os.environ['XIEBOX_INSTALL_BACKUP'],'baseline.json').write_text(json.dumps(baseline))
PY
if ! id -u xiebox >/dev/null 2>&1; then useradd --system --user-group --home-dir /var/lib/xiebox --shell /usr/sbin/nologin xiebox; fi
install -d -m 755 /opt/xiebox /opt/xiebox/releases "$release"
cp -a "$stage/api/." "$release/"
find "$release" -type d -exec chmod 755 {} +
find "$release" -type f -exec chmod 644 {} +
chmod 755 "$release/XieBox.Api"
install -d -o xiebox -g xiebox -m 700 /var/lib/xiebox
chown root:xiebox /etc/xiebox
chmod 750 /etc/xiebox
python3 - <<'PY'
import json,pathlib,secrets,subprocess,os,grp
mysql=['mysql','--defaults-file=/etc/mysql/debian.cnf','--batch','--skip-column-names']
def query(sql): return subprocess.check_output(mysql,input=sql.encode()).decode().strip()
database_exists=query("SELECT COUNT(*) FROM information_schema.schemata WHERE SCHEMA_NAME='xiebox';")!='0'
if database_exists:
    assert os.environ.get('XIEBOX_RESUME_EMPTY_DATABASE')=='1','xiebox database already exists'
    assert query("SELECT COUNT(*) FROM information_schema.tables WHERE TABLE_SCHEMA='xiebox';")=='0','Existing xiebox database is not empty'
assert query("SELECT COUNT(*) FROM mysql.user WHERE User='xiebox_app';")=='0','xiebox_app database account already exists'
secret='Aa1!'+secrets.token_hex(32)
if not database_exists: query("CREATE DATABASE xiebox CHARACTER SET utf8mb4 COLLATE utf8mb4_bin;")
query("CREATE USER 'xiebox_app'@'127.0.0.1' IDENTIFIED BY '"+secret+"'; GRANT SELECT,INSERT,UPDATE,DELETE,CREATE,ALTER,DROP,INDEX,REFERENCES ON xiebox.* TO 'xiebox_app'@'127.0.0.1';")
settings=json.loads(pathlib.Path('/etc/xiebox/mail.json').read_text())
assert settings['Mail'].get('Password'), 'SMTP password has not been configured'
settings.update({'ConnectionStrings':{'PlatformDatabase':'Server=127.0.0.1;Port=3306;Database=xiebox;User ID=xiebox_app;Password='+secret+';SslMode=Required;MaximumPoolSize=8;MinimumPoolSize=0'},'Platform':{'DataDirectory':'/var/lib/xiebox'},'AllowedHosts':'www.xie-bro.top;xie-bro.top;127.0.0.1;localhost','Logging':{'LogLevel':{'Default':'Warning','Microsoft.AspNetCore':'Warning'}}})
fd=os.open('/etc/xiebox/api.json',os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o640)
with os.fdopen(fd,'w') as out: json.dump(settings,out)
os.chown('/etc/xiebox/api.json',0,grp.getgrnam('xiebox').gr_gid)
os.chmod('/etc/xiebox/api.json',0o640)
print('Created isolated xiebox database and restricted service configuration.')
PY
cd "$release"
runuser -u xiebox -- env ASPNETCORE_ENVIRONMENT=Production XIEBOX_SECRETS_FILE=/etc/xiebox/api.json "$release/XieBox.Api" --migrate
mysql --defaults-file=/etc/mysql/debian.cnf -e "REVOKE CREATE,ALTER,DROP,INDEX,REFERENCES ON xiebox.* FROM 'xiebox_app'@'127.0.0.1';"
ln -s "$release" /opt/xiebox/current
cat > /etc/systemd/system/xiebox-api.service <<'UNIT'
[Unit]
Description=XieBro ToolBox account API
After=network-online.target mysql.service
Wants=network-online.target

[Service]
Type=simple
User=xiebox
Group=xiebox
WorkingDirectory=/opt/xiebox/current
ExecStart=/opt/xiebox/current/XieBox.Api
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://127.0.0.1:5188
Environment=XIEBOX_SECRETS_FILE=/etc/xiebox/api.json
Environment=DOTNET_GCHeapHardLimit=0x6000000
Restart=on-failure
RestartSec=5
MemoryHigh=192M
MemoryMax=256M
UMask=0077
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/var/lib/xiebox

[Install]
WantedBy=multi-user.target
UNIT
chmod 644 /etc/systemd/system/xiebox-api.service
service_created=1
systemctl daemon-reload
systemctl enable --now xiebox-api.service
ready=0
for attempt in $(seq 1 30); do
    if curl -fsS --max-time 3 -H 'X-Forwarded-Proto: https' http://127.0.0.1:5188/health > "$backup/health.json"; then ready=1; break; fi
    sleep 1
done
test "$ready" = 1
install -m 644 "$stage/nginx.new" "${config}.xiebox-new"
mv "${config}.xiebox-new" "$config"
config_changed=1
nginx -t
systemctl reload nginx
sleep 1
python3 - <<'PY'
import os,pathlib,json,subprocess,hashlib
baseline=json.loads(pathlib.Path(os.environ['XIEBOX_INSTALL_BACKUP'],'baseline.json').read_text())
for route,old in baseline.items():
    raw=subprocess.check_output(['curl','-sS','--max-time','20','-H','Host: www.xie-bro.top','-w','\n%{http_code}','http://127.0.0.1'+route])
    body,status=raw.rsplit(b'\n',1)
    assert status.decode()==old['status'],route
    if route!='/checkin-app': assert hashlib.sha256(body).hexdigest()==old['hash'],route
    print('Existing site preserved:',route,status.decode())
# Existing TLS chain issue is isolated to this loopback check; validate public HTTPS from the client separately.
base=['curl','-sS','--max-time','20','--insecure','--resolve','www.xie-bro.top:443:127.0.0.1']
health=subprocess.check_output(base+['https://www.xie-bro.top/xie-bro-tool-box/health'])
assert json.loads(health)['status']=='ok'
status=subprocess.check_output(base+['-o','/dev/null','-w','%{http_code}','-H','Content-Type: application/json','-d','{"email":"invalid","password":"123456"}','https://www.xie-bro.top/xie-bro-tool-box/api/v1/auth/register'])
assert status==b'400'
print('Public API route verified without sending email.')
PY
systemctl is-active xiebox-api nginx mysql
systemctl show xiebox-api -p MemoryCurrent -p MainPID
free -m
printf '\nAPI DEPLOYMENT SUCCESS\nURL: https://www.xie-bro.top/xie-bro-tool-box/\nBackup: %s\n' "$backup"
trap - ERR
if [ "${XIEBOX_SEND_TEST_MAIL:-0}" = 1 ]; then
    runuser -u xiebox -- env ASPNETCORE_ENVIRONMENT=Production XIEBOX_SECRETS_FILE=/etc/xiebox/api.json "$release/XieBox.Api" --test-mail
    echo 'AUTHORIZED TEST EMAIL SENT'
fi
