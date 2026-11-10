#!/usr/bin/env bash
# Хук runner перед каждой задачей (#65), ставится в /opt/actions-runner/hooks/. Ненулевой выход — задача падает до первого шага.
# Репозиторий публичный: PR из форка может принести workflow с меткой сервера. Пропускаем только кнопку из master.
if [[ "${GITHUB_EVENT_NAME:-}" != workflow_dispatch || "${GITHUB_REF:-}" != refs/heads/master ]]; then
  echo "::error::Этот runner выполняет только ручной запуск Deploy из master (пришло: ${GITHUB_EVENT_NAME:-?}, ${GITHUB_REF:-?})" >&2
  exit 1
fi
