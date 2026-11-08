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
- делает `pg_dump` в `/var/backups/hh-autoapplier/hhapplier-deploy-<дата>-<коммит>.dump`;
- выполняет checkout в `/opt/hh-autoapplier`, `docker compose up -d --build` и ждёт `/health` (до 3 минут);
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

### Сервер (root)

Подготовлены заранее: runner в `/opt/actions-runner`, unit `/etc/systemd/system/actions-runner.service`, хук `/opt/actions-runner/hooks/job-started.sh`. Новую версию runner берут из [релизов actions/runner](https://github.com/actions/runner/releases), сверяя SHA256 со страницей релиза.

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
