import { spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";

// Работа с Windows-сервером backend по SSH (#5). Хост — псевдоним из ~/.ssh/config (по умолчанию home-pc),
// адрес, пользователь и ключ в репозиторий не попадают.
//   node scripts/server.mjs inspect                 read-only снимок: ОС, ресурсы, Docker, порты, firewall, сон, SSH/RDP
//   node scripts/server.mjs ps "<PowerShell>"       выполнить команду на сервере
//   node scripts/server.mjs ps -f script.ps1        выполнить файл
//   --host <alias>  или  HH_SERVER=<alias>          другой хост;  --print — показать команду ssh, не выполняя
//   --local                                         выполнить на этой машине, без SSH (отладка скриптов)
// Команды идут через -EncodedCommand: без экранирования кавычек и с UTF-8 (кириллица) в выводе.
// Скрипт ничего не ограничивает: изменяющие команды на сервере — только по решению владельца (AGENTS.md).

const PRELUDE = [
  "$ProgressPreference='SilentlyContinue'",
  "[Console]::OutputEncoding=[Text.Encoding]::UTF8",
  "$OutputEncoding=[Text.Encoding]::UTF8",
].join("; ");

// Только чтение. Каждый блок в try: на сервере может не быть Docker, Tailscale и т.п.
const INSPECT = String.raw`
function Section($name, [scriptblock]$body) { "== $name"; try { & $body | Out-String -Width 200 | ForEach-Object { $_.TrimEnd() } } catch { "  ошибка: $($_.Exception.Message)" } }
Section 'Система' {
  $os = Get-CimInstance Win32_OperatingSystem; $cs = Get-CimInstance Win32_ComputerSystem
  "$env:COMPUTERNAME | $($os.Caption) $($os.BuildNumber) | пользователь $(whoami)"
  "Запущен: $($os.LastBootUpTime) | CPU: $((Get-CimInstance Win32_Processor | Select-Object -First 1).Name.Trim()) | RAM: {0:N1} ГБ, свободно {1:N1} ГБ" -f ($cs.TotalPhysicalMemory/1GB), ($os.FreePhysicalMemory/1MB)
}
Section 'Диски' { Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' | ForEach-Object { "{0} {1:N0} ГБ, свободно {2:N0} ГБ" -f $_.DeviceID, ($_.Size/1GB), ($_.FreeSpace/1GB) } }
Section 'Сеть (IPv4)' { Get-NetIPConfiguration | Where-Object IPv4Address | ForEach-Object { "{0}: {1} шлюз {2}" -f $_.InterfaceAlias, ($_.IPv4Address.IPAddress -join ','), $_.IPv4DefaultGateway.NextHop } }
Section 'Сон и питание (AC, сек; 0 = никогда)' {
  foreach ($s in @(@('SUB_SLEEP','STANDBYIDLE','Сон'), @('SUB_SLEEP','HIBERNATEIDLE','Гибернация'))) {
    $q = powercfg /query SCHEME_CURRENT $s[0] $s[1] 2>$null | Select-String '0x[0-9a-f]+$' | Select-Object -Last 2
    if ($q) { "$($s[2]): " + [Convert]::ToInt32(($q[0].Line -split ' ')[-1], 16) } else { "$($s[2]): нет данных" }
  }
}
Section 'Программы' {
  foreach ($c in 'docker','wsl','git','node','dotnet','tailscale','pwsh') {
    $cmd = Get-Command $c -ErrorAction SilentlyContinue
    if ($cmd) { "{0}: {1}" -f $c, $cmd.Source } else { "{0}: нет" -f $c }
  }
}
Section 'Docker' {
  if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { 'Docker не установлен'; return }
  docker version --format 'клиент {{.Client.Version}}, сервер {{.Server.Version}} ({{.Server.Os}})' 2>&1
  docker compose ls 2>&1
  docker ps --format '{{.Names}}  {{.Image}}  {{.Status}}  {{.Ports}}' 2>&1
}
Section 'WSL' { if (Get-Command wsl -ErrorAction SilentlyContinue) { $env:WSL_UTF8 = '1'; wsl --status 2>&1 | Where-Object { "$_".Trim() } } }
Section 'Слушающие порты TCP' {
  Get-NetTCPConnection -State Listen | Where-Object { $_.LocalAddress -notin '::1' -and $_.LocalPort -lt 49152 } |
    Sort-Object LocalPort -Unique | ForEach-Object { "{0,-16} {1,5}  {2}" -f $_.LocalAddress, $_.LocalPort, (Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue).ProcessName }
}
Section 'Firewall' { Get-NetFirewallProfile | ForEach-Object { "{0}: {1}, входящие по умолчанию {2}" -f $_.Name, ($(if ($_.Enabled) {'вкл'} else {'выкл'})), $_.DefaultInboundAction } ; "Профиль сети: " + ((Get-NetConnectionProfile | ForEach-Object { "$($_.InterfaceAlias)=$($_.NetworkCategory)" }) -join ', ') }
Section 'SSH и RDP' {
  $cfg = 'C:\ProgramData\ssh\sshd_config'
  if (-not (Test-Path $cfg)) { 'sshd_config нет' }
  elseif (-not ($c = Get-Content $cfg -ErrorAction SilentlyContinue)) { 'sshd_config: нет доступа (нужны права администратора)' }
  else { $c | Where-Object { $_ -match '^\s*(PasswordAuthentication|PubkeyAuthentication|Match|AuthorizedKeysFile|PermitRootLogin)\b' } | ForEach-Object { "sshd: $($_.Trim())" } }
  "sshd служба: " + (Get-Service sshd -ErrorAction SilentlyContinue).Status
  $ts = Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server'
  $nla = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp').UserAuthentication
  "RDP: " + ($(if ($ts.fDenyTSConnections -eq 0) {'включён'} else {'выключен'})) + ", NLA: " + ($(if ($nla -eq 1) {'вкл'} else {'выкл'}))
}
Section 'Обновления Windows (ожидают перезагрузки)' { Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired' }
`;

function usage() {
  console.log(`Использование:
  node scripts/server.mjs inspect
  node scripts/server.mjs ps "<PowerShell>"
  node scripts/server.mjs ps -f script.ps1
Опции: --host <ssh-псевдоним> (или HH_SERVER, по умолчанию home-pc), --print`);
  process.exit(1);
}

const args = process.argv.slice(2);
let host = process.env.HH_SERVER || "home-pc";
let print = false;
let local = false;
const rest = [];
for (let i = 0; i < args.length; i++) {
  if (args[i] === "--host") host = args[++i];
  else if (args[i] === "--print") print = true;
  else if (args[i] === "--local") local = true;
  else rest.push(args[i]);
}

const [cmd, ...cmdArgs] = rest;
let script;
if (cmd === "inspect") script = INSPECT;
else if (cmd === "ps" && cmdArgs[0] === "-f" && cmdArgs[1]) script = readFileSync(cmdArgs[1], "utf8").replace(/^\uFEFF/, "");
else if (cmd === "ps" && cmdArgs.length) script = cmdArgs.join(" ");
else usage();

const encoded = Buffer.from(`${PRELUDE}\n${script}`, "utf16le").toString("base64");
// Командная строка Windows ограничена 32767 символами (cmd.exe — 8191, если он оболочка sshd).
if (encoded.length > 30000) {
  console.error(`Скрипт слишком длинный для -EncodedCommand (${encoded.length} символов): разбейте его.`);
  process.exit(1);
}

const sshArgs = ["-o", "BatchMode=yes", "-o", "ConnectTimeout=10", host,
  `powershell -NoProfile -NonInteractive -EncodedCommand ${encoded}`];
if (print) {
  console.log(`ssh ${sshArgs.slice(0, -1).join(" ")} "powershell -NoProfile -NonInteractive -EncodedCommand <${encoded.length} символов>"`);
  console.log("--- скрипт ---\n" + script.trim());
  process.exit(0);
}

// --local: тот же скрипт на этой машине, без SSH (отладка скриптов, когда сервер недоступен).
const [exe, exeArgs] = local
  ? ["powershell", ["-NoProfile", "-NonInteractive", "-EncodedCommand", encoded]]
  : ["ssh", sshArgs];
const res = spawnSync(exe, exeArgs, { encoding: "utf8", maxBuffer: 64 * 1024 * 1024 });
if (res.error) {
  console.error(`ssh не запустился: ${res.error.message}`);
  process.exit(1);
}
process.stdout.write(res.stdout ?? "");
process.stderr.write(decodeClixml(res.stderr ?? ""));
process.exit(res.status ?? 1);

// Ошибки неинтерактивного PowerShell приходят в stderr как CLIXML — достаём из него текст.
function decodeClixml(text) {
  if (!text.includes("#< CLIXML")) return text;
  const out = [];
  for (const m of text.matchAll(/<S S="Error">([\s\S]*?)<\/S>/g)) {
    out.push(m[1].replace(/_x([0-9A-F]{4})_/g, (_, h) => String.fromCharCode(parseInt(h, 16)))
      .replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&amp;/g, "&"));
  }
  return out.join("").replace(/\r?\n$/, "") + (out.length ? "\n" : "") + text.replace(/#< CLIXML[\s\S]*<\/Objs>/g, "").trim();
}
