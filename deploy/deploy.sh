#!/usr/bin/env bash
# Деплой backend (#65): запускается из .github/workflows/deploy.yml на self-hosted runner, инструкция — docs/DEPLOY.md.
#
# Порядок: выбрать коммит → проверить зелёный CI именно этого коммита → бэкап БД → checkout → docker compose up --build
# → дождаться /health. Не поднялось — откат на предыдущий коммит, выход с ошибкой. До checkout сервер не меняется.
#
# Логи Actions публичны: логи приложения сюда не выводить (при сбое они сохраняются в файл на сервере).
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/hh-autoapplier}"
BACKUP_DIR="${BACKUP_DIR:-/var/backups/hh-autoapplier}"
HEALTH_URL="${HEALTH_URL:-http://127.0.0.1:8080/health}"
HEALTH_TIMEOUT="${HEALTH_TIMEOUT:-180}"
DEPLOY_REF="${DEPLOY_REF:-master}"
DEPLOY_BACKUP="${DEPLOY_BACKUP:-true}"
CI_WORKFLOW=".github/workflows/ci.yml"

outcome="Отказ: сервер не менялся"
reason=""
before=""
target=""
after=""
health="не проверялось"
backup_file="нет"

git_app() { git -C "$APP_DIR" "$@"; }
compose() { (cd "$APP_DIR/server" && docker compose "$@"); }

write_summary() {
  [[ -n "${GITHUB_STEP_SUMMARY:-}" ]] || return 0
  {
    echo "## Деплой: $outcome"
    [[ -z "$reason" ]] || echo "$reason"
    echo
    echo "| | |"
    echo "|---|---|"
    echo "| Запрошено | \`$DEPLOY_REF\` |"
    echo "| Было | \`${before:-?}\` |"
    echo "| Выкатывался | \`${target:-?}\` |"
    echo "| Стало | \`${after:-${before:-?}}\` |"
    echo "| /health | \`$health\` |"
    echo "| Бэкап БД | \`$backup_file\` |"
  } >>"$GITHUB_STEP_SUMMARY"
}
trap write_summary EXIT

die() {
  reason="$*"
  echo "::error::$*" >&2
  exit 1
}

# Ветка или тег с GitHub, либо SHA, достижимый из них. Коммит, которого нет ни в одной ветке или теге (например,
# из форка: GitHub отдаёт такие по SHA), не выкатываем.
resolve_target() {
  local ref=$1 sha
  [[ $ref =~ ^[A-Za-z0-9][A-Za-z0-9._/-]*$ ]] || die "Недопустимые символы в ref"
  if sha=$(git_app rev-parse --verify --quiet "refs/remotes/origin/$ref^{commit}"); then
    target=$sha
  elif sha=$(git_app rev-parse --verify --quiet "refs/tags/$ref^{commit}"); then
    target=$sha
  elif [[ $ref =~ ^[0-9a-f]{7,40}$ ]] && sha=$(git_app rev-parse --verify --quiet "$ref^{commit}") \
    && [[ -n $(git_app for-each-ref --count=1 --contains "$sha" refs/remotes/origin refs/tags) ]]; then
    target=$sha
  else
    die "Ref '$ref' не найден среди веток и тегов репозитория"
  fi
}

# Сверяем head_sha: зелёный прогон другого коммита не считается. Отменённые прогоны пропускаем (новый push в ту же ветку
# отменяет старый), из остальных решает самый свежий: один коммит мог проверяться в нескольких ветках.
check_ci() {
  local sha=$1 json run status conclusion url
  json=$(curl -fsSL --retry 3 -m 30 \
    -H "Accept: application/vnd.github+json" \
    -H "Authorization: Bearer ${GITHUB_TOKEN:?нужен GITHUB_TOKEN}" \
    "${GITHUB_API_URL:-https://api.github.com}/repos/${GITHUB_REPOSITORY:?нужен GITHUB_REPOSITORY}/actions/runs?head_sha=$sha&per_page=100") \
    || die "Не удалось получить прогоны CI из GitHub API"
  run=$(jq -c --arg sha "$sha" --arg path "$CI_WORKFLOW" \
    '[.workflow_runs[] | select(.head_sha == $sha and .path == $path and .conclusion != "cancelled")] | sort_by(.created_at) | last // empty' \
    <<<"$json")
  [[ -n "$run" ]] || die "Нет прогона CI для коммита $sha: деплой запрещён"
  status=$(jq -r '.status' <<<"$run")
  conclusion=$(jq -r '.conclusion' <<<"$run")
  url=$(jq -r '.html_url' <<<"$run")
  [[ "$status" == completed ]] || die "CI для коммита $sha ещё не завершён ($status): $url"
  [[ "$conclusion" == success ]] || die "CI для коммита $sha не зелёный ($conclusion): $url"
  echo "CI зелёный для $sha: $url"
}

backup_db() {
  local file
  mkdir -p "$BACKUP_DIR"
  file="$BACKUP_DIR/hhapplier-deploy-$(date +%Y%m%d-%H%M%S)-${before:0:7}.dump"
  # </dev/null: иначе exec -T читает stdin шага
  if ! (umask 077 && compose exec -T db pg_dump -U hhapplier -d hhapplier -Fc </dev/null >"$file.tmp") || [[ ! -s "$file.tmp" ]]; then
    rm -f "$file.tmp"
    die "Бэкап БД не удался: выкат отменён, сервер не менялся"
  fi
  mv "$file.tmp" "$file"
  backup_file="$file"
  echo "Бэкап БД: $file"
}

# COMPOSE_FILE в server/.env (читается только эта строка) должен ссылаться на файлы, которые есть в выкатываемом
# коммите. Иначе docker compose упадёт уже после checkout: так было бы с docker-compose.vpn-happ.yml, удалённым в #84.
# Проверяем до бэкапа, пока сервер не менялся
check_compose_files() {
  local sha=$1 line files f
  local -a list
  line=$(grep -E '^[[:space:]]*COMPOSE_FILE=' "$APP_DIR/server/.env" 2>/dev/null | tail -n 1) || return 0
  files=${line#*=}
  files=${files//$'\r'/}
  files=${files//\"/}
  files=${files//\'/}
  IFS=':' read -ra list <<<"$files"
  for f in "${list[@]}"; do
    f=${f#./}
    [[ -n "$f" ]] || continue
    git_app cat-file -e "$sha:server/$f" 2>/dev/null \
      || die "COMPOSE_FILE в server/.env ссылается на $f, а в выкатываемом коммите его нет: уберите его из .env (docs/DEPLOY.md)"
  done
}

# Три успешных ответа подряд: контейнер в цикле перезапусков может ответить один раз
wait_health() {
  local deadline=$((SECONDS + HEALTH_TIMEOUT)) ok=0 response code
  while ((SECONDS < deadline)); do
    response=$(curl -sS -m 3 -w '\n%{http_code}' "$HEALTH_URL" 2>/dev/null) || response=$'\n000'
    code=${response##*$'\n'}
    health="$code ${response%$'\n'*}"
    if [[ "$code" == 200 ]]; then
      ok=$((ok + 1))
      if ((ok >= 3)); then return 0; fi
    else
      ok=0
    fi
    sleep 2
  done
  return 1
}

roll_out() {
  local sha=$1
  git_app checkout --quiet --detach "$sha" || return 1
  after=$sha
  # Коммит сборки для GET /api/version (#77): без него в ответе «unknown»
  GIT_COMMIT=$(git_app rev-parse --short "$sha") || return 1
  export GIT_COMMIT
  compose up -d --build || return 1
  wait_health
}

# Контейнер при откате пересоздаётся и его логи пропадают, а в Actions их выводить нельзя
save_failure_logs() {
  local file
  mkdir -p "$BACKUP_DIR"
  file="$BACKUP_DIR/deploy-failed-$(date +%Y%m%d-%H%M%S)-${target:0:7}.log"
  (umask 077 && compose logs --no-color --tail=500 api </dev/null >"$file" 2>&1) || true
  echo "$file"
}

before=$(git_app rev-parse HEAD)
[[ -z $(git_app status --porcelain) ]] || die "В $APP_DIR есть локальные правки: деплой отменён"

git_app fetch --quiet --prune --force origin '+refs/heads/*:refs/remotes/origin/*' '+refs/tags/*:refs/tags/*' \
  || die "Не удалось получить обновления с GitHub"
resolve_target "$DEPLOY_REF"
echo "Было: $before, выкатываем: $target ($DEPLOY_REF)"

check_ci "$target"
check_compose_files "$target"
if [[ "$DEPLOY_BACKUP" == true ]]; then backup_db; else backup_file="отключён"; fi

if roll_out "$target"; then
  outcome="Успех"
  exit 0
fi

failure_log=$(save_failure_logs)
echo "::error::Выкат $target не прошёл (/health: $health)" >&2

if [[ "$target" == "$before" ]]; then
  outcome="Сбой: откатывать некуда"
  reason="Выкатывался уже установленный коммит. Логи на сервере: $failure_log"
  exit 1
fi

failed_health=$health
if roll_out "$before"; then
  outcome="Сбой: выполнен откат"
  reason="Коммит не поднялся (/health: $failed_health), вернули предыдущий. Логи на сервере: $failure_log"
else
  outcome="Сбой: откат НЕ удался, нужна ручная починка"
  reason="См. docs/DEPLOY.md. Логи на сервере: $failure_log"
fi
exit 1
