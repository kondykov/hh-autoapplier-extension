#!/usr/bin/env bash
# Службы сервера (#5): relay прокси Happ для контейнеров и ежедневный бэкап базы. Запускать от root, порядок — docs/DEPLOY.md.
# Только раскладывает файлы и перечитывает systemd: включает службы владелец отдельными командами.
# Повторный запуск обновляет файлы. Скрипт бэкапа копируется из коммита и не зависит от того, что выкачено в /opt.
set -euo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)

install -d -m 755 /usr/local/lib/hh-autoapplier
install -m 755 "$HERE/backup.sh" /usr/local/lib/hh-autoapplier/backup.sh
for unit in hh-proxy-relay.socket hh-proxy-relay.service hh-backup.service hh-backup.timer; do
  install -m 644 "$HERE/$unit" "/etc/systemd/system/$unit"
done
systemctl daemon-reload

echo "Установлено. Включить (docs/DEPLOY.md): systemctl enable --now hh-proxy-relay.socket hh-backup.timer"
