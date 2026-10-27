# Creates the requested local test account; production refuses this initialization mode.
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath (Split-Path $PSScriptRoot -Parent)
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_CLI_HOME = Join-Path $PWD '.dotnet'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
if (-not $env:ConnectionStrings__PlatformDatabase -and (Test-Path -LiteralPath (Join-Path $PWD '.local/mysql/accounts.xml'))) {
    . (Join-Path $PSScriptRoot 'Start-LocalMySql.ps1')
}
$taskPreviousPassword = $env:XIEBOX_ADMIN_PASSWORD
try {
    $env:XIEBOX_ADMIN_PASSWORD = 'admin123'
    dotnet run --project backend/Api/XieBox.Api.csproj --no-build --no-launch-profile -- --bootstrap-dev-admin
    if ($LASTEXITCODE -ne 0) { throw 'Development administrator initialization failed.' }
} finally {
    if ($null -eq $taskPreviousPassword) { Remove-Item Env:XIEBOX_ADMIN_PASSWORD -ErrorAction SilentlyContinue }
    else { $env:XIEBOX_ADMIN_PASSWORD = $taskPreviousPassword }
}
