param([string]$ExportDirectory = (Join-Path ([Environment]::GetFolderPath('Desktop')) '蟹小箱发布'))
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $taskRoot
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.dotnet'
$env:MSBuildEnableWorkloadResolver = 'false'
$taskOutput = Join-Path $taskRoot 'artifacts/release/XieBroToolBox-single'
dotnet publish XieBox.csproj -c Release -r win-x64 -p:PortableRelease=true -p:DebugType=None -p:DebugSymbols=false -o $taskOutput -m:1 --nologo
if ($LASTEXITCODE -ne 0) { throw '单文件发布失败' }
$taskFiles = @(Get-ChildItem -LiteralPath $taskOutput -File -Recurse)
if ($taskFiles.Count -ne 1 -or $taskFiles[0].Name -ne 'XieBroToolBox.exe') { throw '发布目录不是单 EXE，请检查输出' }
New-Item -ItemType Directory -Path $ExportDirectory -Force | Out-Null
[xml]$taskProject = Get-Content -LiteralPath (Join-Path $taskRoot 'XieBox.csproj') -Raw
$taskVersion = @($taskProject.Project.PropertyGroup.Version | Where-Object { $_ })[0]
if ($taskVersion -notmatch '^\d+\.\d+(\.\d+)?$') { throw '无效的发布版本号' }
$taskExport = Join-Path $ExportDirectory "XieBroToolBox-$taskVersion-win-x64.exe"
# Copy bytes so exported files inherit the normal destination security descriptor.
$taskInput = [IO.File]::OpenRead($taskFiles[0].FullName)
try { $taskStream = [IO.File]::Create($taskExport); try { $taskInput.CopyTo($taskStream) } finally { $taskStream.Dispose() } } finally { $taskInput.Dispose() }
Get-Item -LiteralPath $taskExport | Select-Object FullName,Length
Get-FileHash -LiteralPath $taskExport -Algorithm SHA256
