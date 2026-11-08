#!/usr/bin/env python3
# Собирает xray/outbound.json из ссылки vless://… (#5). Ссылка вводится скрыто и нигде не выводится.
# Запуск на сервере от root: python3 xray/vless-to-outbound.py [путь, по умолчанию xray/outbound.json рядом со скриптом]
import getpass
import json
import os
import sys
import tempfile
from urllib.parse import parse_qs, unquote, urlsplit

XRAY_UID = 65532  # пользователь xray в образе


def fail(message):
    sys.exit(f"Ошибка: {message}")


def build(link):
    parts = urlsplit(link.strip())
    if parts.scheme != "vless":
        fail(f"нужна ссылка vless://, получена схема «{parts.scheme or 'нет'}»")
    if not parts.username or not parts.hostname or not parts.port:
        fail("в ссылке нет id, адреса или порта")

    query = {k: v[-1] for k, v in parse_qs(parts.query).items()}
    network = query.get("type", "tcp")
    security = query.get("security", "none")

    user = {"id": unquote(parts.username), "encryption": query.get("encryption", "none")}
    if query.get("flow"):
        user["flow"] = query["flow"]

    stream = {"network": network, "security": security}
    if security == "reality":
        if not query.get("pbk"):
            fail("для reality в ссылке нет pbk")
        stream["realitySettings"] = {
            "serverName": query.get("sni", ""),
            "fingerprint": query.get("fp", "chrome"),
            "publicKey": query["pbk"],
            "shortId": query.get("sid", ""),
            "spiderX": query.get("spx", ""),
        }
    elif security == "tls":
        tls = {"serverName": query.get("sni", parts.hostname), "fingerprint": query.get("fp", "chrome")}
        if query.get("alpn"):
            tls["alpn"] = query["alpn"].split(",")
        stream["tlsSettings"] = tls
    elif security != "none":
        fail(f"security «{security}» не поддерживается скриптом")

    if network == "ws":
        stream["wsSettings"] = {"path": query.get("path", "/"), "host": query.get("host", "")}
    elif network == "grpc":
        stream["grpcSettings"] = {"serviceName": query.get("serviceName", "")}
    elif network == "xhttp":
        stream["xhttpSettings"] = {"path": query.get("path", "/"), "host": query.get("host", ""), "mode": query.get("mode", "auto")}
    elif network != "tcp":
        fail(f"транспорт «{network}» не поддерживается скриптом")

    outbound = {
        "tag": "vpn",
        "protocol": "vless",
        "settings": {"vnext": [{"address": parts.hostname, "port": parts.port, "users": [user]}]},
        "streamSettings": stream,
    }
    return {"outbounds": [outbound]}, network, security


def main():
    target = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "outbound.json")
    link = sys.stdin.readline() if not sys.stdin.isatty() else getpass.getpass("Ссылка vless:// (ввод скрыт): ")
    config, network, security = build(link)

    fd, tmp = tempfile.mkstemp(dir=os.path.dirname(os.path.abspath(target)))
    with os.fdopen(fd, "w") as f:
        json.dump(config, f, indent=2)
        f.write("\n")
    os.chmod(tmp, 0o600)
    if os.geteuid() == 0:
        os.chown(tmp, XRAY_UID, XRAY_UID)
    os.replace(tmp, target)
    print(f"Записано: {target} (vless, {network}, {security}). Дальше: docker compose up -d xray")


if __name__ == "__main__":
    main()
