#!/usr/bin/env bash
# Update an existing installation using a full, checksummed api/ bundle.
set -Eeuo pipefail
umask 077
stage=$(cd -- "$(dirname -- "$0")" && pwd)
stamp=$(date -u +%Y%m%dT%H%M%SZ)
release=/opt/xiebox/releases/$stamp
backup=/var/backups/xiebox-api/$stamp
previous=$(readlink -f /opt/xiebox/current)
case "$previous" in /opt/xiebox/releases/*) ;; *) echo 'Unexpected current release'; exit 1;; esac
test -x "$previous/XieBox.Api"
test -f /etc/xiebox/api.json
systemctl is-active --quiet xiebox-api
test "$(systemctl show xiebox-api -p User --value)" = xiebox
test ! -e "$release"
test ! -e /opt/xiebox/current.next
nginx -t
mkdir -p "$backup"
printf '%s\n' "$previous" > "$backup/previous-release.txt"
sha256sum /etc/nginx/conf.d/personal-projects.conf > "$backup/nginx.sha256"
curl -fsS --max-time 15 -H 'Host: www.xie-bro.top' http://127.0.0.1/xie-bro-tool-box/ > "$backup/website.html"
(cd "$stage/api" && tr -d '\r' < ../files.sha256 | sha256sum -c - > "$backup/package-check.txt")
changed=0
rollback() {
    rc=$?; trap - ERR
    if [ "$changed" = 1 ]; then
        ln -s "$previous" /opt/xiebox/current.rollback
        mv -Tf /opt/xiebox/current.rollback /opt/xiebox/current
        systemctl restart xiebox-api
    fi
    echo "Update stopped; previous release retained. Backup: $backup"
    exit "$rc"
}
trap rollback ERR
install -d -m 755 "$release"
cp -a "$stage/api/." "$release/"
find "$release" -type d -exec chmod 755 {} +
find "$release" -type f -exec chmod 644 {} +
chmod 755 "$release/XieBox.Api"
ln -s "$release" /opt/xiebox/current.next
mv -Tf /opt/xiebox/current.next /opt/xiebox/current
changed=1
systemctl restart xiebox-api
ready=0
for attempt in $(seq 1 30); do
    if curl -fsS --max-time 3 -H 'X-Forwarded-Proto: https' http://127.0.0.1:5188/health > "$backup/health.json" 2>/dev/null; then ready=1; break; fi
    sleep 1
done
test "$ready" = 1
python3 - "$backup/health.json" <<'PY'
import json,sys
assert json.load(open(sys.argv[1]))['status']=='ok'
PY
sha256sum -c "$backup/nginx.sha256"
curl -fsS --max-time 15 -H 'Host: www.xie-bro.top' http://127.0.0.1/xie-bro-tool-box/ > "$backup/website-after.html"
cmp "$backup/website.html" "$backup/website-after.html"
systemctl is-active xiebox-api nginx mysql
systemctl show xiebox-api -p MemoryCurrent -p MainPID
printf '\nAPI UPDATE SUCCESS\nRelease: %s\nPrevious: %s\nBackup: %s\nNo test email sent.\n' "$release" "$previous" "$backup"
