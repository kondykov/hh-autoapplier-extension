import { spawn } from "node:child_process";
import { readFileSync } from "node:fs";

// MCP-сервер Postgres для агента (подключается из .mcp.json): crystaldba/postgres-mcp в режиме restricted (только чтение).
// Пароль берётся из server/.env, в репозиторий не попадает; контейнер ходит в базу по сети docker compose.
const env = Object.fromEntries(
  readFileSync("server/.env", "utf8")
    .split(/\r?\n/)
    .map((l) => l.match(/^([A-Z_]+)=(.*)$/))
    .filter(Boolean)
    .map((m) => [m[1], m[2]]),
);
const password = encodeURIComponent(env.POSTGRES_PASSWORD || "hhapplier");

const child = spawn(
  "docker",
  ["run", "-i", "--rm", "--network", "server_default", "-e", "DATABASE_URI",
    "crystaldba/postgres-mcp:0.3.0", "--access-mode=restricted"],
  { stdio: "inherit", env: { ...process.env, DATABASE_URI: `postgresql://hhapplier:${password}@db:5432/hhapplier` } },
);
child.on("exit", (code) => process.exit(code ?? 1));
