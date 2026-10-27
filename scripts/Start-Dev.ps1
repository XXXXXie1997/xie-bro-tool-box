param([switch]$BackendOnly, [switch]$DesktopOnly, [switch]$OpenCleaner, [switch]$OpenScanner)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $taskRoot
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.dotnet'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
if (-not $env:XIEBOX_API_URL) { $env:XIEBOX_API_URL = 'http://127.0.0.1:5188/' }
if (-not $BackendOnly) {
    $taskDesktopExe = Join-Path $taskRoot 'bin/Debug/net10.0-windows/XieBroToolBox.exe'
    $taskExistingDesktop = Get-Process XieBox -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $taskDesktopExe } | Select-Object -First 1
    if ($taskExistingDesktop) {
        # Reuse the running desktop before building its locked executable or starting another API.
        if ($OpenScanner) { & $taskDesktopExe --open-scanner } elseif ($OpenCleaner) { & $taskDesktopExe --open-cleaner } else { & $taskDesktopExe }
        exit $LASTEXITCODE
    }
}
if (-not $DesktopOnly) {
    if (-not $env:ConnectionStrings__PlatformDatabase -and (Test-Path -LiteralPath (Join-Path $taskRoot '.local/mysql/accounts.xml'))) {
        . (Join-Path $PSScriptRoot 'Start-LocalMySql.ps1')
    }
    dotnet build backend/Api/XieBox.Api.csproj --no-restore -m:1
    if ($LASTEXITCODE -ne 0) { throw '后台构建失败，请先执行 dotnet restore。' }
    if ($BackendOnly) { dotnet run --project backend/Api/XieBox.Api.csproj --no-build; exit $LASTEXITCODE }
    $apiLog = Join-Path $taskRoot 'artifacts'
    New-Item -ItemType Directory -Path $apiLog -Force | Out-Null
    $apiProcess = Start-Process -FilePath 'dotnet' -ArgumentList @('run', '--project', 'backend/Api/XieBox.Api.csproj', '--no-build') -WorkingDirectory $taskRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $apiLog 'api.log') -RedirectStandardError (Join-Path $apiLog 'api-error.log') -PassThru
}
try {
    dotnet build XieBox.csproj --no-restore -m:1
    if ($LASTEXITCODE -ne 0) { throw '桌面构建失败。' }
    if ($OpenScanner) { dotnet run --project XieBox.csproj --no-build -- --open-scanner } elseif ($OpenCleaner) { dotnet run --project XieBox.csproj --no-build -- --open-cleaner } else { dotnet run --project XieBox.csproj --no-build }
    if ($LASTEXITCODE -ne 0) { throw '桌面平台未正常退出。' }
} finally {
    if ($apiProcess -and -not $apiProcess.HasExited) { Stop-Process -Id $apiProcess.Id }
}
