# Shared helpers for the project-local Windows MySQL installation.
$script:localMySqlProject = Split-Path $PSScriptRoot -Parent
$script:localMySqlState = Join-Path $script:localMySqlProject '.local/mysql'
$script:localMySqlBin = Join-Path $script:localMySqlProject '.local/mysql-8.4.11-winx64/bin'

function Get-XieBoxMySqlAccounts {
    $path = Join-Path $script:localMySqlState 'accounts.xml'
    if (-not (Test-Path -LiteralPath $path)) { throw '尚未初始化本地 MySQL。' }
    Import-Clixml -LiteralPath $path
}

function Set-XieBoxMySqlEnvironment {
    $accounts = Get-XieBoxMySqlAccounts
    $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
    $connection['Server'] = '127.0.0.1'
    $connection['Port'] = 3306
    $connection['Database'] = 'xiebox'
    $connection['User ID'] = $accounts.Application.UserName
    $connection['Password'] = $accounts.Application.GetNetworkCredential().Password
    $connection['SslMode'] = 'Required'
    $env:ConnectionStrings__PlatformDatabase = $connection.ConnectionString
    $connection.Clear()
}

function Invoke-XieBoxMySqlRoot([string]$Sql) {
    $accounts = Get-XieBoxMySqlAccounts
    $previousPassword = $env:MYSQL_PWD
    try {
        $env:MYSQL_PWD = $accounts.Root.GetNetworkCredential().Password
        $Sql | & (Join-Path $script:localMySqlBin 'mysql.exe') --no-defaults --host=127.0.0.1 --port=3306 --user=root --ssl-mode=REQUIRED --batch --skip-column-names
        if ($LASTEXITCODE -ne 0) { throw '本地 MySQL 管理命令失败。' }
    } finally {
        if ($null -eq $previousPassword) { Remove-Item Env:MYSQL_PWD -ErrorAction SilentlyContinue }
        else { $env:MYSQL_PWD = $previousPassword }
    }
}

function Start-XieBoxMySql([string]$InitFile = '') {
    $pidFile = Join-Path $script:localMySqlState 'mysqld.pid'
    if (Test-Path -LiteralPath $pidFile) {
        $serverId = [int](Get-Content -LiteralPath $pidFile -Raw).Trim()
        $existing = Get-Process -Id $serverId -ErrorAction SilentlyContinue
        if ($existing -and $existing.Path -eq (Join-Path $script:localMySqlBin 'mysqld.exe')) {
            Invoke-XieBoxMySqlRoot 'SELECT 1;' | Out-Null
            return
        }
    }
    $config = Join-Path $script:localMySqlState 'my.ini'
    $arguments = @('--defaults-file="' + $config + '"')
    if ($InitFile) { $arguments += '--init-file="' + $InitFile + '"' }
    $server = Start-Process -FilePath (Join-Path $script:localMySqlBin 'mysqld.exe') -ArgumentList $arguments -WorkingDirectory $script:localMySqlState -WindowStyle Hidden -PassThru
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        Start-Sleep -Milliseconds 500
        if ($server.HasExited) { throw 'MySQL 启动失败，请检查 .local/mysql/mysql-error.log。' }
        if (Test-Path -LiteralPath $pidFile) {
            try {
                Invoke-XieBoxMySqlRoot 'SELECT 1;' 2>$null | Out-Null
                return
            } catch { }
        }
    }
    throw 'MySQL 启动超时，请检查 .local/mysql/mysql-error.log。'
}
