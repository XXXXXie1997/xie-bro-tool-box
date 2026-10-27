$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Start-LocalMySql.ps1')
Set-Location -LiteralPath (Split-Path $PSScriptRoot -Parent)
$testSuffix = [Guid]::NewGuid().ToString('N')
$testDatabase = 'xiebox_test_' + $testSuffix
$testUser = 'xt_' + $testSuffix.Substring(0, 20)
$bytes = New-Object byte[] 32
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($bytes); $testPassword = [Convert]::ToBase64String($bytes) } finally { $rng.Dispose() }
Invoke-XieBoxMySqlRoot @"
CREATE DATABASE $testDatabase CHARACTER SET utf8mb4 COLLATE utf8mb4_bin;
CREATE USER '$testUser'@'localhost' IDENTIFIED BY '$testPassword' REQUIRE SSL;
GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, DROP, INDEX, REFERENCES ON $testDatabase.* TO '$testUser'@'localhost';
"@ | Out-Null
$connection = [System.Data.Common.DbConnectionStringBuilder]::new()
$connection['Server'] = '127.0.0.1'
$connection['Port'] = 3306
$connection['Database'] = $testDatabase
$connection['User ID'] = $testUser
$connection['Password'] = $testPassword
$connection['SslMode'] = 'Required'
$previousTestConnection = $env:XIEBOX_TEST_MYSQL_CONNECTION
try {
    $env:XIEBOX_TEST_MYSQL_CONNECTION = $connection.ConnectionString
    python -u backend/tests/integration.py
    if ($LASTEXITCODE -ne 0) { throw 'MySQL 集成测试未通过，请检查 artifacts/api-tests/ 的最近日志。' }
    Write-Output ('测试数据库保留供检查：' + $testDatabase)
} finally {
    if ($null -eq $previousTestConnection) { Remove-Item Env:XIEBOX_TEST_MYSQL_CONNECTION -ErrorAction SilentlyContinue }
    else { $env:XIEBOX_TEST_MYSQL_CONNECTION = $previousTestConnection }
    $connection.Clear()
    $testPassword = $null
}
