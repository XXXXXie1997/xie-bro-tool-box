$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LocalMySql.ps1')
$pidFile = Join-Path $script:localMySqlState 'mysqld.pid'
if (-not (Test-Path -LiteralPath $pidFile)) {
    Write-Output '本地 MySQL 已处于停止状态。'
    return
}
$serverId = [int](Get-Content -LiteralPath $pidFile -Raw).Trim()
$server = Get-Process -Id $serverId -ErrorAction SilentlyContinue
if (-not $server -or $server.Path -ne (Join-Path $script:localMySqlBin 'mysqld.exe')) {
    Remove-Item -LiteralPath $pidFile
    Write-Output '本地 MySQL 已停止，已清理失效进程记录。'
    return
}
# SQL shutdown uses the credentials of this project instance, rather than killing an arbitrary PID.
Invoke-XieBoxMySqlRoot 'SHUTDOWN;'
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    if (-not (Test-Path -LiteralPath $pidFile)) { break }
    Start-Sleep -Milliseconds 250
}
if (Test-Path -LiteralPath $pidFile) { throw 'MySQL 仍在关闭，请稍后检查 .local/mysql/mysql-error.log。' }
Write-Output '本地 MySQL 已停止，数据库文件保留。'
