#!/usr/bin/env bash
# Подготовка self-hosted runner на сервере (#65): запускать от root один раз, порядок — docs/DEPLOY.md.
# Только раскладывает файлы: учётная запись, регистрация и запуск службы — отдельными шагами владельца.
set -euo pipefail

VERSION=2.338.2
SHA256=baebf204307bac6193480852732f0aba2ae97b6c5597c244f4d5558390147c40
RUNNER_DIR=/opt/actions-runner
HERE=$(cd "$(dirname "$0")" && pwd)

[[ ! -e "$RUNNER_DIR" ]] || { echo "$RUNNER_DIR уже существует, ничего не меняю" >&2; exit 1; }

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
curl -fsSL --retry 3 -o "$tmp/runner.tar.gz" "https://github.com/actions/runner/releases/download/v$VERSION/actions-runner-linux-x64-$VERSION.tar.gz"
echo "$SHA256  $tmp/runner.tar.gz" | sha256sum -c -

mkdir "$RUNNER_DIR"
tar xzf "$tmp/runner.tar.gz" -C "$RUNNER_DIR" --no-same-owner
install -d -m 755 "$RUNNER_DIR/hooks"
install -m 755 "$HERE/job-started.sh" "$RUNNER_DIR/hooks/job-started.sh"
install -m 644 "$HERE/actions-runner.service" /etc/systemd/system/actions-runner.service
systemctl daemon-reload

echo "Runner $("$RUNNER_DIR/bin/Runner.Listener" --version) готов в $RUNNER_DIR, служба пока не включена"
