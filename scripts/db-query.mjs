import { spawnSync } from "node:child_process";

// Read-only запрос к базе docker compose (server/docker-compose.yml): node scripts/db-query.mjs "select ..."
// Транзакции по умолчанию read-only, поэтому запись упадёт с ошибкой; это защита от случайностей, а не граница доступа.
const sql = process.argv.slice(2).join(" ").trim();
if (!sql) {
  console.log('Использование: node scripts/db-query.mjs "select count(*) from ..."   (\\dt — список таблиц)');
  process.exit(1);
}

const res = spawnSync(
  "docker",
  [
    "compose", "-f", "server/docker-compose.yml", "exec", "-T",
    "-e", "PGOPTIONS=-c default_transaction_read_only=on",
    "db", "psql", "-U", "hhapplier", "-d", "hhapplier", "-v", "ON_ERROR_STOP=1", "-P", "pager=off", "-c", sql,
  ],
  { stdio: "inherit" },
);
process.exit(res.status ?? 1);
