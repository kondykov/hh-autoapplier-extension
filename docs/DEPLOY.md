# Деплой backend на сервер

Деплой запускается только вручную: Actions → **Deploy** → **Run workflow**. Автоматического выката после push нет. На сервере работает self-hosted GitHub runner: он сам подключается к GitHub, входящие порты не нужны.

Workflow — [`.github/workflows/deploy.yml`](../.github/workflows/deploy.yml), логика — [`deploy/deploy.sh`](../deploy/deploy.sh).

## Как запустить

1. Actions → Deploy → Run workflow, «Use workflow from» — `master`.
2. В поле «Что выкатить» указать ветку, тег или SHA (по умолчанию `master`). Флажок «Бэкап БД» оставить включённым.
3. Итог смотреть в Summary запуска: что было, что стало, ответ `/health`, файл бэкапа.

Что делает скрипт:

- выбирает коммит: только из веток и тегов этого репозитория;
- проверяет CI именно этого коммита: нет прогона, идёт, красный — отказ, сервер не меняется;
- проверяет, что файлы из `COMPOSE_FILE` в `server/.env` есть в выкатываемом коммите, иначе отказ до бэкапа;
- делает `pg_dump` в `/var/backups/hh-autoapplier/hhapplier-deploy-<дата>-<коммит>.dump`;
- выполняет checkout в `/opt/hh-autoapplier`, `docker compose up -d --build` с `GIT_COMMIT` выкатываемого коммита (его показывает `GET /api/version`) и ждёт `/health` (до 3 минут);
- при сбое сохраняет логи API в `/var/backups/hh-autoapplier/deploy-failed-*.log`, возвращает предыдущий коммит, задача становится красной. GitHub присылает письмо о сбое; уведомление в Telegram — после #28.

Одновременно идёт только один деплой. Логи Actions в публичном репозитории видны всем, поэтому логи приложения в них не выводятся.

## Как откатить

Запустить Deploy и в поле ref указать SHA нужной версии (видно в Summary прошлых запусков, поле «Было»). Откат идёт тем же путём и с той же проверкой CI.

Миграции при откате кода назад не отменяются. Если новая версия изменила схему и старая с ней не работает, восстановите базу из бэкапа, сделанного перед выкатом (от root на сервере):

```bash
cd /opt/hh-autoapplier/server
docker compose stop api
docker compose exec -T db pg_restore -U hhapplier -d hhapplier --clean --if-exists < /var/backups/hh-autoapplier/ФАЙЛ.dump
docker compose start api
```

Если задача красная с текстом «откат НЕ удался» или деплой был отменён посреди выката, сервер остаётся в промежуточном состоянии. Проверьте `git -c safe.directory='*' -C /opt/hh-autoapplier log -1` и `docker compose ps`, затем запустите Deploy заведомо рабочей версии.

## Первоначальная настройка

Секреты (`POSTGRES_PASSWORD`, ключ LLM, токен Telegram) лежат только в `server/.env` на сервере. В секреты GitHub их не добавлять.

### Исходящий прокси и бэкапы (сервер, root)

С российского IP Groq и Telegram напрямую не работают, hh.ru работает. VPN — окружение машины (#84): на home-server это Happ, его HTTP-прокси слушает только `127.0.0.1:10809`, и контейнеру он не виден. Relay `hh-proxy-relay` принимает соединения на `172.17.0.1:10810` (адрес хоста в docker0) и передаёт их в Happ. API получает `HTTPS_PROXY` из `server/.env`, hh.ru идёт напрямую через `NO_PROXY`. Ежедневный бэкап базы — `hh-backup.timer`, хранится 7 дней.

Файлы — в [`deploy/`](../deploy/), ставит их [`install-host-units.sh`](../deploy/install-host-units.sh). `РЕВИЗИЯ` — ветка или тег с этими файлами:

```bash
git -C /opt/hh-autoapplier fetch origin РЕВИЗИЯ
mkdir /root/hh-units && git -C /opt/hh-autoapplier archive FETCH_HEAD deploy | tar -x -C /root/hh-units
bash /root/hh-units/deploy/install-host-units.sh
rm -rf /root/hh-units
```

**Переход с контейнера xray**, если сервер настраивался по старой схеме. Сейчас работающий API уже ходит на `host.docker.internal:10810`, поэтому relay на том же адресе подхватывает его без перезапуска:

1. Happ подключён: `curl -s -o /dev/null -w '%{http_code}\n' -x http://127.0.0.1:10809 https://api.telegram.org/` — не `000`.
2. Убрать xray и включить relay: `docker rm -f server-xray-1`, затем `systemctl enable --now hh-proxy-relay.socket`.
3. Через relay: `curl -s -o /dev/null -w '%{http_code}\n' -x http://172.17.0.1:10810 https://api.telegram.org/` — не `000`.
4. В `server/.env`: удалить строку `COMPOSE_FILE`; добавить `HTTPS_PROXY=http://host.docker.internal:10810` и `NO_PROXY=hh.ru,.hh.ru,db,localhost,127.0.0.1`; очистить `ADMIN_PASSWORD` и `CORS_ORIGINS`. Пока `COMPOSE_FILE` указывает на удалённый файл, `deploy.sh` откажет до бэкапа, а `docker compose` в каталоге не работает.
5. Правило ufw для relay уже есть, если его нет: `ufw allow in from 172.16.0.0/12 to 172.17.0.1 port 10810 proto tcp`.

**Бэкапы:** `systemctl enable --now hh-backup.timer`, проверить сразу — `systemctl start hh-backup.service && ls -lt /var/backups/hh-autoapplier | head -3`. Журнал: `journalctl -u hh-backup.service -n 20`.

**Выкат вручную**, пока `deploy.yml` нет в `master` и кнопки ещё нет. `ТЕГ` — например `backend-v0.2.0-rc.4`:

```bash
(umask 077 && docker exec server-db-1 pg_dump -U hhapplier -d hhapplier -Fc > /var/backups/hh-autoapplier/pre-deploy-$(date +%Y%m%d-%H%M).dump)
git -C /opt/hh-autoapplier fetch --tags origin
git -C /opt/hh-autoapplier checkout --detach ТЕГ
cd /opt/hh-autoapplier/server && GIT_COMMIT=$(git rev-parse --short HEAD) docker compose up -d --build
curl -s http://127.0.0.1:8080/health
```

Relay зависит от Happ: после перезагрузки Happ сам не подключается, пока не выполнен вход в KDE, и до этого Groq и Telegram недоступны. Как это решить — `docs/specs/sprint-15.md`, В3.

### Runner (сервер, root)

Скрипт [`deploy/install-runner.sh`](../deploy/install-runner.sh) скачивает runner в `/opt/actions-runner`, сверяет SHA256, ставит unit и хук. Версия и хеш записаны в скрипте (значения — со [страницы релиза actions/runner](https://github.com/actions/runner/releases)). Дальше runner обновляется сам.

Рабочая копия приложения не трогается, скрипт берётся из коммита во временный каталог. `РЕВИЗИЯ` — ветка с `deploy/`, после слияния `master`:

```bash
git -C /opt/hh-autoapplier fetch origin РЕВИЗИЯ
mkdir /root/hh-deploy && git -C /opt/hh-autoapplier archive FETCH_HEAD deploy | tar -x -C /root/hh-deploy
bash /root/hh-deploy/deploy/install-runner.sh
rm -rf /root/hh-deploy
```

Учётная запись и права:

```bash
useradd --system --create-home --shell /usr/sbin/nologin --groups docker deploy
chown -R deploy:deploy /opt/hh-autoapplier /var/backups/hh-autoapplier /opt/actions-runner
chown -R root:root /opt/actions-runner/hooks
```

Регистрация runner — токен из Settings → Actions → Runners → New self-hosted runner (живёт час):

```bash
cd /opt/actions-runner
runuser -u deploy -- ./config.sh --url https://github.com/kondykov/hh-autoapplier-extension --token ТОКЕН --name home-server --labels home-server --work _work --unattended
systemctl enable --now actions-runner.service
systemctl status actions-runner --no-pager
```

Группа `docker` по правам равна root на этой машине, поэтому главная защита — чтобы на runner не попал чужой код.

После смены владельца `git` от root ругается на чужой каталог: для просмотра используйте `git -c safe.directory='*' ...`, менять репозиторий вручную не нужно.

### GitHub (Settings)

- **Actions → General → Fork pull request workflows**: «Require approval for all external contributors».
- **Actions → General → Workflow permissions**: «Read repository contents and packages permissions» (уже стоит).
- **Environments → New environment** `production` → Deployment branches and tags → Selected → `master`.
- Кнопка появляется, только когда `deploy.yml` есть в ветке по умолчанию (`master`).

## Защита

Репозиторий публичный, и PR из форка мог бы принести workflow с меткой сервера. Поэтому:

- в `deploy.yml` единственный триггер — `workflow_dispatch`, запустить его может только тот, у кого есть запись в репозиторий;
- хук runner `job-started.sh` роняет любую задачу, кроме ручного запуска из `master`, ещё до первого шага;
- обычный CI работает на `ubuntu-latest`. Метку `home-server` в других workflow не использовать;
- runner работает под `deploy`, а не под root.
