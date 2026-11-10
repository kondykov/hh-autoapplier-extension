#!/usr/bin/env bash
# Ежедневный бэкап базы (#5): запускает hh-backup.timer, ставится deploy/install-host-units.sh в /usr/local/lib/hh-autoapplier.
# Хранит KEEP_DAYS дней только свои файлы hhapplier-daily-*; дампы перед выкатом (deploy.sh) не трогает.
# Восстановление — docs/DEPLOY.md, «Как откатить».
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/hh-autoapplier}"
BACKUP_DIR="${BACKUP_DIR:-/var/backups/hh-autoapplier}"
KEEP_DAYS="${KEEP_DAYS:-7}"

# docker exec, а не docker compose: compose читает .env и падает, если COMPOSE_FILE указывает на удалённый файл.
# Таймер с Persistent=true срабатывает сразу после загрузки: база могла ещё не подняться — ждём до двух минут
# DB_CONTAINER — имя контейнера вручную (проверка на другой машине)
db=""
for _ in $(seq 1 24); do
  db=${DB_CONTAINER:-$(docker ps -q --filter "label=com.docker.compose.project.working_dir=$APP_DIR/server" --filter "label=com.docker.compose.service=db")}
  [[ -n "$db" ]] && docker exec "$db" pg_isready -q -U hhapplier -d hhapplier && break
  db=""
  sleep 5
done
[[ -n "$db" ]] || { echo "Контейнер базы не запущен или не готов: бэкап не сделан" >&2; exit 1; }

mkdir -p "$BACKUP_DIR"
file="$BACKUP_DIR/hhapplier-daily-$(date +%Y%m%d-%H%M%S).dump"
umask 077
trap 'rm -f "$file.tmp"' EXIT
docker exec "$db" pg_dump -U hhapplier -d hhapplier -Fc </dev/null >"$file.tmp"
[[ -s "$file.tmp" ]] || { echo "Пустой дамп: бэкап не сделан" >&2; exit 1; }
mv "$file.tmp" "$file"
echo "Бэкап: $file ($(du -h "$file" | cut -f1))"

find "$BACKUP_DIR" -maxdepth 1 -type f -name 'hhapplier-daily-*.dump' -mtime +"$KEEP_DAYS" -print -delete
