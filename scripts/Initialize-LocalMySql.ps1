$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LocalMySql.ps1')
if (Test-Path -LiteralPath $script:localMySqlState) { throw '本地 MySQL 目录已存在，拒绝重新初始化或覆盖数据。' }
$serverExe = Join-Path $script:localMySqlBin 'mysqld.exe'
if (-not (Test-Path -LiteralPath $serverExe)) { throw '请先下载官方 MySQL 8.4.11 Windows ZIP 并解压到 .local。' }
$signature = Get-AuthenticodeSignature -FilePath $serverExe
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Oracle') { throw 'MySQL 发布者签名校验失败。' }
$portProbe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 3306)
try { $portProbe.Start() } finally { $portProbe.Stop() }
New-Item -ItemType Directory -Path $script:localMySqlState | Out-Null
$currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
& icacls.exe $script:localMySqlState /inheritance:r /grant:r ('*' + $currentSid + ':(OI)(CI)F') '*S-1-5-18:(OI)(CI)F' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'MySQL 数据目录权限设置失败。' }
function New-LocalPassword {
    $bytes = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes); [Convert]::ToBase64String($bytes) } finally { $rng.Dispose() }
}
$rootPassword = New-LocalPassword
$appPassword = New-LocalPassword
$accounts = @{
    Root = [PSCredential]::new('root', (ConvertTo-SecureString $rootPassword -AsPlainText -Force))
    Application = [PSCredential]::new('xiebox', (ConvertTo-SecureString $appPassword -AsPlainText -Force))
}
$accounts | Export-Clixml -LiteralPath (Join-Path $script:localMySqlState 'accounts.xml')
$basePath = (Split-Path $script:localMySqlBin -Parent).Replace('\', '/')
$statePath = $script:localMySqlState.Replace('\', '/')
@"
[mysqld]
basedir="$basePath"
datadir="$statePath/data"
port=3306
bind-address=127.0.0.1
mysqlx=OFF
character-set-server=utf8mb4
collation-server=utf8mb4_bin
log-error="$statePath/mysql-error.log"
pid-file="$statePath/mysqld.pid"
innodb-buffer-pool-size=128M
max-connections=40
local-infile=OFF
secure-file-priv=NULL
"@ | Set-Content -LiteralPath (Join-Path $script:localMySqlState 'my.ini') -Encoding ASCII
& $serverExe ('--defaults-file=' + (Join-Path $script:localMySqlState 'my.ini')) --initialize-insecure
if ($LASTEXITCODE -ne 0) { throw 'MySQL 数据目录初始化失败。' }
$initFile = Join-Path $script:localMySqlState 'initialize.sql'
@"
ALTER USER 'root'@'localhost' IDENTIFIED BY '$rootPassword' REQUIRE SSL;
CREATE DATABASE xiebox CHARACTER SET utf8mb4 COLLATE utf8mb4_bin;
CREATE USER 'xiebox'@'localhost' IDENTIFIED BY '$appPassword' REQUIRE SSL;
GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, DROP, INDEX, REFERENCES ON xiebox.* TO 'xiebox'@'localhost';
"@ | Set-Content -LiteralPath $initFile -Encoding ASCII
try {
    Start-XieBoxMySql -InitFile $initFile
    Set-XieBoxMySqlEnvironment
    Write-Output 'MySQL 已初始化并启动：127.0.0.1:3306，数据库 xiebox。凭据由 Windows DPAPI 加密保存。'
} finally {
    Remove-Item -LiteralPath $initFile -ErrorAction SilentlyContinue
    $rootPassword = $null
    $appPassword = $null
}
