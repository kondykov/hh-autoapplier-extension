import { spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";

// Работа с сервером backend по SSH (#5). Хост — псевдоним из ~/.ssh/config (по умолчанию home-server-root):
// адрес, пользователь и ключ в репозиторий не попадают.
//   node scripts/server.mjs inspect                 read-only снимок: ОС, ресурсы, Docker, порты, firewall, сон, SSH, обновления
//   node scripts/server.mjs ps "<PowerShell>"       выполнить команду на сервере
//   node scripts/server.mjs ps -f script.ps1        выполнить файл
//   --host <alias>  или  HH_SERVER=<alias>          другой хост
//   --windows                                       сервер на Windows: Windows PowerShell 5.1 вместо pwsh
//   --local                                         выполнить на этой машине, без SSH (отладка скриптов)
//   --print                                         показать команду ssh и скрипт, не выполняя
// На сервере запускается PowerShell 7 (pwsh). Скрипт идёт в stdin (base64 от UTF-8), по командной строке — только
// короткий загрузчик в -EncodedCommand: без экранирования кавычек и предела длины (у cmd.exe на Windows — 8191 символ),
// кириллица в выводе не ломается.
// Скрипт ничего не ограничивает: изменяющие команды на сервере — только по решению владельца (AGENTS.md).

const PRELUDE = [
  "$ProgressPreference='SilentlyContinue'",
  "[Console]::OutputEncoding=[Text.Encoding]::UTF8",
  "$OutputEncoding=[Text.Encoding]::UTF8",
  // pwsh 7: без ANSI-цветов в выводе и ошибках
  "if ($PSStyle) { $PSStyle.OutputRendering='PlainText' }",
].join("; ");

// Только чтение. Каждый блок в try: на сервере может не быть Docker, ufw и т.п. Ветка по ОС — $IsLinux (pwsh 7);
// в Windows PowerShell 5.1 переменной нет, и выполняется ветка Windows.
const INSPECT = String.raw`
function Section($name, [scriptblock]$body) { "== $name"; try { & $body | Out-String -Width 200 | ForEach-Object { $_.TrimEnd() } } catch { "  ошибка: $($_.Exception.Message)" } }
# Внешняя команда: на Linux — одна строка для bash -c (аргументы вида -r при вызове функции разбирает сам
# PowerShell), на Windows — как есть. Вывод и ошибки — строками, без записей ErrorRecord.
function X {
  if ($IsLinux) { bash -c ($args -join ' ') 2>&1 | ForEach-Object { "$_" } }
  else { $exe, $rest = $args; & $exe @rest 2>&1 | ForEach-Object { "$_" } }
}
function Has($c) { [bool](Get-Command $c -ErrorAction SilentlyContinue) }

if ($IsLinux) {
  $root = (X 'id -u') -eq '0'
  Section 'Система' {
    $os = (Get-Content /etc/os-release | ConvertFrom-StringData).PRETTY_NAME.Trim('"')
    "$(X hostname) | $os | $(X 'uname -r') | пользователь $(X whoami)$(if (-not $root) { ' (не root: часть данных недоступна)' })"
    "Запущен: $(X 'uptime -s') | CPU: $(((Get-Content /proc/cpuinfo | Select-String -Pattern '^model name' | Select-Object -First 1) -split ':\s*', 2)[1])"
    X 'free -h'
  }
  Section 'Диски' { X 'df -h -x tmpfs -x devtmpfs -x efivarfs -x squashfs -x overlay' }
  Section 'Загрузка и сон' {
    "Режим загрузки: $(if (Test-Path /sys/firmware/efi) { 'UEFI' } else { 'Legacy BIOS' }), цель по умолчанию: $(X 'systemctl get-default')"
    'Сон: ' + ((('sleep','suspend','hibernate','hybrid-sleep') | ForEach-Object { "$_=$(X "systemctl is-enabled $_.target")" }) -join ' ')
  }
  Section 'Сеть (IPv4)' { X 'ip -4 -br addr' | Where-Object { $_ -notmatch '^lo\s' }; X 'ip route show default' }
  Section 'Программы' {
    foreach ($c in 'pwsh','docker','git','curl','ufw','nft','tailscale','unattended-upgrade','node','dotnet') {
      $cmd = Get-Command $c -ErrorAction SilentlyContinue
      if ($cmd) { "{0}: {1}" -f $c, $cmd.Source } else { "{0}: нет" -f $c }
    }
  }
  Section 'Docker' {
    if (-not (Has docker)) { 'Docker не установлен'; return }
    "Служба: $(X 'systemctl is-active docker'), автозапуск: $(X 'systemctl is-enabled docker')"
    "Docker $(X "docker version --format '{{.Server.Version}}'"), compose $(X 'docker compose version --short')"
    X 'docker compose ls'
    X "docker ps --format '{{.Names}}  {{.Image}}  {{.Status}}  {{.Ports}}'"
    X 'docker system df'
  }
  Section 'Слушающие порты TCP' { X 'ss -tlnpH' | ForEach-Object { $f = $_ -split '\s+'; "{0,-22} {1}" -f $f[3], (($f[5] -replace '^users:\(\("', '') -replace '".*$', '') } | Sort-Object -Unique }
  Section 'Firewall' {
    if (Has ufw) { X 'ufw status verbose' } else { 'ufw не установлен' }
    if ($root -and (Has nft)) { "Правил nftables: " + (X 'nft list ruleset' | Select-String -Pattern '^\s+(ip|tcp|udp|ct|iif|oif|meta)\b').Count }
  }
  Section 'SSH' {
    if ($root) { X 'sshd -T' | Where-Object { $_ -match '^(passwordauthentication|kbdinteractiveauthentication|permitrootlogin|pubkeyauthentication|port) ' } }
    else { 'sshd -T: нужен root' }
    "Служба ssh: $(X 'systemctl is-active ssh')"
  }
  Section 'Обновления' {
    $up = X 'apt list --upgradable 2>/dev/null' | Where-Object { $_ -match '/' }
    "Пакетов к обновлению: $(@($up).Count) (по последнему apt update)"
    "Нужна перезагрузка: $(if (Test-Path /run/reboot-required) { 'да' } else { 'нет' })"
    "unattended-upgrades: $(if (Has unattended-upgrade) { X 'systemctl is-enabled unattended-upgrades' } else { 'не установлен' })"
  }
  Section 'Ошибки с последней загрузки (journalctl -p err, последние 5)' { X 'journalctl -p err -b --no-pager -n 5 -o short-iso' }
  return
}

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
  if (-not (Has docker)) { 'Docker не установлен'; return }
  X docker version --format 'клиент {{.Client.Version}}, сервер {{.Server.Version}} ({{.Server.Os}})'
  X docker compose ls
  X docker ps --format '{{.Names}}  {{.Image}}  {{.Status}}  {{.Ports}}'
}
Section 'WSL' {
  # wsl.exe пишет в UTF-16 (и сообщение «не установлен» тоже) — читаем его вывод как Unicode.
  $prev = [Console]::OutputEncoding; [Console]::OutputEncoding = [Text.Encoding]::Unicode
  # Прямой вызов, не через X: Windows PowerShell принимает --status за параметр функции.
  try { $o = wsl --status 2>&1 | ForEach-Object { "$_" }; $code = $LASTEXITCODE } finally { [Console]::OutputEncoding = $prev }
  if ($code -ne 0) { 'WSL не установлен или не работает (wsl --status: код ' + $code + ')' }
  $o | Where-Object { $_.Trim() -and $_ -ne 'System.Management.Automation.RemoteException' }
}
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
Опции: --host <ssh-псевдоним> (или HH_SERVER, по умолчанию home-server-root), --windows, --local, --print`);
  process.exit(1);
}

const args = process.argv.slice(2);
let host = process.env.HH_SERVER || "home-server-root";
let print = false;
let local = false;
let windows = false;
const rest = [];
for (let i = 0; i < args.length; i++) {
  if (args[i] === "--host") host = args[++i];
  else if (args[i] === "--print") print = true;
  else if (args[i] === "--local") local = true;
  else if (args[i] === "--windows") windows = true;
  else rest.push(args[i]);
}

const [cmd, ...cmdArgs] = rest;
let script;
if (cmd === "inspect") script = INSPECT;
else if (cmd === "ps" && cmdArgs[0] === "-f" && cmdArgs[1]) script = readFileSync(cmdArgs[1], "utf8").replace(/^\uFEFF/, "");
else if (cmd === "ps" && cmdArgs.length) script = cmdArgs.join(" ");
else usage();

const BOOTSTRAP = [
  PRELUDE,
  "$s = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([Console]::In.ReadToEnd().Trim()))",
  "& ([scriptblock]::Create($s))",
  "if (-not $?) { exit 1 }",
].join("; ");
const encodedBootstrap = Buffer.from(BOOTSTRAP, "utf16le").toString("base64");
const payload = Buffer.from(script, "utf8").toString("base64");
const shell = windows ? "powershell" : "pwsh";
const psArgs = ["-NoProfile", "-NonInteractive", "-EncodedCommand", encodedBootstrap];

const sshArgs = ["-o", "BatchMode=yes", "-o", "ConnectTimeout=10", host, `${shell} ${psArgs.join(" ")}`];
if (print) {
  console.log(`ssh ${sshArgs.slice(0, -1).join(" ")} "${shell} -NoProfile -NonInteractive -EncodedCommand <загрузчик>"  < скрипт в base64`);
  console.log("--- скрипт ---\n" + script.trim());
  process.exit(0);
}

// --local: тот же скрипт на этой машине (отладка, когда сервер недоступен).
const [exe, exeArgs] = local ? [shell, psArgs] : ["ssh", sshArgs];
const res = spawnSync(exe, exeArgs, { input: payload, encoding: "utf8", maxBuffer: 64 * 1024 * 1024 });
if (res.error) {
  console.error(`${exe} не запустился: ${res.error.message}`);
  process.exit(1);
}
// pwsh 7 красит ошибки ANSI-последовательностями даже при PlainText — убираем их.
const noAnsi = (t) => t.replace(/\x1b\[[0-9;?]*[A-Za-z]/g, "");
process.stdout.write(noAnsi(res.stdout ?? ""));
process.stderr.write(noAnsi(decodeClixml(res.stderr ?? "")));
process.exit(res.status ?? 1);

// Ошибки неинтерактивного Windows PowerShell приходят в stderr как CLIXML — достаём из него текст.
function decodeClixml(text) {
  if (!text.includes("#< CLIXML")) return text;
  const out = [];
  for (const m of text.matchAll(/<S S="Error">([\s\S]*?)<\/S>/g)) {
    out.push(m[1].replace(/_x([0-9A-F]{4})_/g, (_, h) => String.fromCharCode(parseInt(h, 16)))
      .replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&amp;/g, "&"));
  }
  return out.join("").replace(/\r?\n$/, "") + (out.length ? "\n" : "") + text.replace(/#< CLIXML[\s\S]*<\/Objs>/g, "").trim();
}
