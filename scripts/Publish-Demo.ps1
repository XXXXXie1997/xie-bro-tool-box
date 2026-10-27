param([switch]$SkipPublish, [ValidatePattern("^[A-Za-z0-9-]+$")][string]$OutputName = "XieBox-Demo-win-x64")
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $taskRoot
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.dotnet'
$taskOutput = Join-Path $taskRoot ("artifacts/release/" + $OutputName)
$taskAppOutput = Join-Path $taskOutput 'app'
if (-not $SkipPublish) {
    dotnet publish XieBox.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $taskAppOutput -m:1
    if ($LASTEXITCODE -ne 0) { throw '发布失败' }
}
[IO.File]::WriteAllText((Join-Path $taskAppOutput 'Resources/platformsettings.json'), '{ "apiBaseUrl": "http://127.0.0.1:5188/", "dataDirectory": null, "demoEdition": true }', [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $taskAppOutput 'Resources/catalog.json'), '[]', [Text.UTF8Encoding]::new($false))
# Remove only the obsolete root configuration copied by the normal development build.
$taskRootSettings = Join-Path $taskAppOutput 'platformsettings.json'
if (Test-Path -LiteralPath $taskRootSettings) { Remove-Item -LiteralPath $taskRootSettings }
$taskCompiler = Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $taskCompiler /nologo /target:winexe /optimize+ /reference:System.Windows.Forms.dll ("/win32icon:" + (Join-Path $taskRoot 'Resources/app.ico')) ("/out:" + (Join-Path $taskOutput 'XieBox.exe')) (Join-Path $taskRoot 'scripts/OfflineLauncher.cs')
if ($LASTEXITCODE -ne 0) { throw '启动程序编译失败' }
$readme = @"
蟹小箱 · 离线演示版

适用于 Windows 10 22H2 / Windows 11，64 位电脑。
将整个压缩包解压到一个文件夹，双击 XieBox.exe 即可运行。
已包含 .NET 运行时，不需要另装 .NET、MySQL 或启动后台服务。
根目录 XieBox.exe 为启动入口，app 文件夹存放完整运行组件和配置，请勿拆分或删除。
用户数据仍保存在 LocalAppData，不写入发布目录。

默认包含垃圾清理、文件扫描、启动项管理、内存释放、磁盘空间分析、软件卸载、批量重命名、文件整理、上网排查九个应用，首页集成电脑体检，隐藏登录入口。
垃圾清理为真实操作，确认后会删除所选文件。
文件扫描支持大小、格式和重复内容查找，不删除或移动文件。
磁盘空间分析为逻辑大小汇总，不代表实际可释放空间。
软件卸载调用原卸载程序，商店应用请使用系统应用设置。
批量重命名和文件整理先预览再执行，不覆盖重名文件，保留上次操作的撤销记录。
文件整理仅处理当前层普通文件，按类型移动；隐藏、系统、链接和离线文件不处理。
上网排查默认检查网络连通性，也可单独测试网站链接并保存排查报告。
启动项管理支持当前用户启动项的关闭和恢复；所有用户启动项暂只读。
内存释放按需整理后台进程工作集，不结束应用，实际效果随系统状态变化。

点击窗口右上角关闭按钮会收起到系统托盘；右键托盘图标可退出。
设置保存在当前用户的 LocalAppData\XieBoxDemo，与开发版分开。
邮箱、QQ群入口目前为占位。
"@
[IO.File]::WriteAllText((Join-Path $taskOutput '使用说明.txt'), $readme, [Text.UTF8Encoding]::new($true))
$taskPreviousData = $env:XIEBOX_DATA_DIR
try {
    $env:XIEBOX_DATA_DIR = Join-Path $taskRoot ('artifacts/demo-smoke/' + [Guid]::NewGuid().ToString('N'))
    $taskStart = [Diagnostics.ProcessStartInfo]::new((Join-Path $taskOutput 'XieBox.exe'))
    $taskStart.Arguments = '--smoke-test'
    $taskStart.WorkingDirectory = $taskOutput
    $taskStart.UseShellExecute = $false
    $taskSmoke = [Diagnostics.Process]::Start($taskStart)
    if (-not $taskSmoke.WaitForExit(60000)) { Stop-Process -InputObject $taskSmoke; throw '演示包启动测试超时' }
    if ($taskSmoke.ExitCode -ne 0) { throw "演示包启动失败：$($taskSmoke.ExitCode)" }
} finally { $env:XIEBOX_DATA_DIR = $taskPreviousData }
$taskZip = Join-Path $taskRoot ("artifacts/release/" + $OutputName + ".zip")
Compress-Archive -LiteralPath $taskOutput -DestinationPath $taskZip -CompressionLevel Optimal -Force
Get-Item -LiteralPath $taskZip | Select-Object FullName, Length
Get-FileHash -LiteralPath $taskZip -Algorithm SHA256

