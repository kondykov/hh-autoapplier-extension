import { execSync, spawnSync } from "node:child_process";

// PreToolUse-хук на `git commit`: если в индексе есть файлы расширения, сначала typecheck и тесты.
// Exit 2 блокирует коммит и возвращает агенту вывод ошибок; backend (dotnet test) не гоняем — ему нужен Docker.
const staged = execSync("git diff --cached --name-only", { encoding: "utf8" }).split("\n").filter(Boolean);
const touchesExtension = staged.some((f) => /^(src|tests)\/|^(package(-lock)?\.json|tsconfig\.json|vitest\.config\.ts)$/.test(f));
if (!touchesExtension) process.exit(0);

for (const script of ["typecheck", "test"]) {
  const res = spawnSync(`npm run ${script}`, { encoding: "utf8", shell: true });
  if (res.status !== 0) {
    process.stderr.write(`npm run ${script} упал — коммит остановлен:\n${(res.stdout + res.stderr).slice(-4000)}`);
    process.exit(2);
  }
}
