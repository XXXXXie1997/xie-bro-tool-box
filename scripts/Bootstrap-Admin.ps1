param([Parameter(Mandatory)][string]$Username)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath (Split-Path $PSScriptRoot -Parent)
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_CLI_HOME = Join-Path $PWD '.dotnet'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
if (-not $env:ConnectionStrings__PlatformDatabase -and (Test-Path -LiteralPath (Join-Path $PWD '.local/mysql/accounts.xml'))) {
    . (Join-Path $PSScriptRoot 'Start-LocalMySql.ps1')
}
$taskPassword = Read-Host '管理员密码（至少 6 位）' -AsSecureString
$taskPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($taskPassword)
try {
    $env:XIEBOX_ADMIN_USERNAME = $Username
    $env:XIEBOX_ADMIN_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($taskPointer)
    dotnet run --project backend/Api/XieBox.Api.csproj --no-launch-profile -- --bootstrap-admin
    if ($LASTEXITCODE -ne 0) { throw '管理员初始化失败。' }
} finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($taskPointer)
    Remove-Item Env:XIEBOX_ADMIN_USERNAME -ErrorAction SilentlyContinue
    Remove-Item Env:XIEBOX_ADMIN_PASSWORD -ErrorAction SilentlyContinue
}
