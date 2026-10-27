param([string]$Compiler = '', [switch]$SkipPublish, [string]$ExportDirectory = (Join-Path ([Environment]::GetFolderPath('Desktop')) '蟹小箱发布'))
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $taskRoot
if (-not $Compiler) { $Compiler = Join-Path $taskRoot 'artifacts/installer-tools/inno/app/ISCC.exe' }
if (-not (Test-Path -LiteralPath $Compiler)) { throw '请用 -Compiler 指定 Inno Setup ISCC.exe 路径。' }
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.dotnet'
$env:MSBuildEnableWorkloadResolver = 'false'
$taskPayload = Join-Path $taskRoot 'artifacts/release/installer-payload'
if (-not $SkipPublish) {
    dotnet publish XieBox.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $taskPayload 'app') -m:1
    if ($LASTEXITCODE -ne 0) { throw '发布失败' }
}
[IO.File]::WriteAllText((Join-Path $taskPayload 'app/Resources/platformsettings.json'), '{ "apiBaseUrl": "http://127.0.0.1:5188/", "dataDirectory": null, "demoEdition": true }')
[IO.File]::WriteAllText((Join-Path $taskPayload 'app/Resources/catalog.json'), '[]')
$taskOldConfig = Join-Path $taskPayload 'app/platformsettings.json'
if (Test-Path -LiteralPath $taskOldConfig) { Remove-Item -LiteralPath $taskOldConfig }
$taskCsc = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $taskCsc /nologo /target:winexe /optimize+ /reference:System.Windows.Forms.dll ('/win32icon:' + (Join-Path $taskRoot 'Resources/app.ico')) ('/out:' + (Join-Path $taskPayload 'XieBox.exe')) (Join-Path $taskRoot 'scripts/OfflineLauncher.cs')
if ($LASTEXITCODE -ne 0) { throw '启动入口编译失败' }
& $taskCsc /nologo /target:winexe /optimize+ /reference:System.Windows.Forms.dll ('/out:' + (Join-Path $taskPayload 'app/SetupActions.exe')) (Join-Path $taskRoot 'scripts/installer/SetupActions.cs')
if ($LASTEXITCODE -ne 0) { throw '快捷方式助手编译失败' }
$taskWpf = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/WPF'
$taskRefs = @('/reference:System.Windows.Forms.dll', ('/reference:' + $taskWpf + '/PresentationFramework.dll'), ('/reference:' + $taskWpf + '/PresentationCore.dll'), ('/reference:' + $taskWpf + '/WindowsBase.dll'), '/reference:System.Xaml.dll')
$taskSource = Join-Path $taskRoot 'scripts/installer/InstallerWindow.cs'
$taskRefs += '/reference:System.Management.dll'
$taskLogo = '/resource:' + (Join-Path $taskRoot 'Resources/logo.png') + ',Logo'
$taskIcon = '/win32icon:' + (Join-Path $taskRoot 'Resources/app.ico')
& $taskCsc /nologo /target:winexe /optimize+ /nowarn:0162,0429 /define:UNINSTALLER $taskRefs $taskLogo $taskIcon ('/out:' + (Join-Path $taskPayload 'app/Uninstall.exe')) $taskSource
if ($LASTEXITCODE -ne 0) { throw '卸载界面编译失败' }
$taskManifest = Join-Path $taskPayload 'app/installed-files.txt'
$taskFiles = @(Get-ChildItem -LiteralPath $taskPayload -File -Recurse | ForEach-Object { $_.FullName.Substring($taskPayload.Length + 1) })
$taskFiles += 'app\installed-files.txt'
[IO.File]::WriteAllLines($taskManifest, @($taskFiles | Sort-Object -Unique))
& $Compiler (Join-Path $taskRoot 'scripts/installer/XieBox.iss')
if ($LASTEXITCODE -ne 0) { throw '安装引擎编译失败' }
$taskSetup = Join-Path $taskRoot 'artifacts/release/XieBox-Setup-1.0.0-v3-win-x64.exe'
& $taskCsc /nologo /target:winexe /optimize+ /nowarn:0162,0429 $taskRefs $taskLogo $taskIcon ('/resource:' + (Join-Path $taskRoot 'artifacts/release/XieBox-InstallEngine.exe') + ',Engine') ('/out:' + $taskSetup) $taskSource
if ($LASTEXITCODE -ne 0) { throw '安装界面编译失败' }
# Export bytes, not development directory ACLs or low-integrity labels.
New-Item -ItemType Directory -Path $ExportDirectory -Force | Out-Null
$taskExport = Join-Path $ExportDirectory (Split-Path $taskSetup -Leaf)
$taskInput = [IO.File]::OpenRead($taskSetup)
try { $taskOutput = [IO.File]::Create($taskExport); try { $taskInput.CopyTo($taskOutput) } finally { $taskOutput.Dispose() } } finally { $taskInput.Dispose() }
Get-Item -LiteralPath $taskExport | Select-Object FullName, Length
Get-FileHash -LiteralPath $taskExport -Algorithm SHA256
