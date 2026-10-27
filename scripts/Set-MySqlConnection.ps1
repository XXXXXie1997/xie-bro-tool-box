param(
    [string]$Server = '127.0.0.1',
    [ValidateRange(1, 65535)][int]$Port = 3306,
    [string]$Database = 'xiebox',
    [string]$User = 'xiebox',
    [ValidateSet('VerifyFull', 'Required', 'Preferred')][string]$SslMode = 'VerifyFull',
    [switch]$ForIntegrationTest
)
$ErrorActionPreference = 'Stop'
if ($ForIntegrationTest -and $Database -cnotmatch '^xiebox_test_[a-z0-9_]+$') {
    throw '集成测试必须使用独立的 xiebox_test_* 数据库。'
}
$securePassword = Read-Host 'MySQL 密码（仅存于当前 PowerShell 进程环境）' -AsSecureString
$connection = [System.Data.Common.DbConnectionStringBuilder]::new()
$connection['Server'] = $Server
$connection['Port'] = $Port
$connection['Database'] = $Database
$connection['User ID'] = $User
$connection['Password'] = [System.Net.NetworkCredential]::new('', $securePassword).Password
$connection['SslMode'] = $SslMode
try {
    if ($ForIntegrationTest) {
        $env:XIEBOX_TEST_MYSQL_CONNECTION = $connection.ConnectionString
    } else {
        $env:ConnectionStrings__PlatformDatabase = $connection.ConnectionString
    }
    Write-Host 'MySQL 连接已配置到当前终端环境，未写入文件。'
} finally {
    $connection.Clear()
    $securePassword.Dispose()
}
