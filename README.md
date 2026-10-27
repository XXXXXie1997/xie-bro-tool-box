# 蟹小箱

面向电脑新手的模块化 Windows 工具箱。平台统一提供导航、模块安装与账号体系，子应用放在 `tools/` 下独立开发。免费本地功能支持游客与离线使用。

## 当前版本：0.1 开发预览

已实现：

- WPF 主窗口、首页、应用介绍页、模块管理页和账号窗口；淡蓝色主题、简洁留白，顶栏与主体分色。标题栏及导航空白支持原生窗口拖拽，按钮和滚动条保留操作。
- 顶部模块入口、隐藏/显示、按钮排序和本地持久化。
- 系统托盘后台运行：点击关闭或 Alt+F4 收起窗口，保留当前页面与任务；双击图标或右键「打开蟹小箱」恢复。右键「退出」才结束程序，有任务时先请求取消并等待结束。重复启动恢复同一用户和数据目录的原窗口。
- 模块发布目录查询、离线目录缓存、下载进度、取消、SHA-256 校验、安全解包、声明式 JSON 内容页面加载及卸载。
- 平台统一用户名登录 / 注册入口：已有用户名验证密码，新用户名直接创建账号并登录；密码为 6–128 位。支持退出、短期访问会话及刷新轮换，本地凭证使用 Windows DPAPI 保存。
- `IPlatformContext` 用户上下文与能力检查；免费能力可以离线判断，模块不持有平台密码或刷新凭证。
- ASP.NET Core + Identity + EF Core + MySQL 后台，独立管理会话、用户启停、会话撤销、模块发布/停用与审计页面。
- 可重复运行的模块包安全测试、导航持久化测试、真实 HTTP 账号/管理权限测试与 WPF 页面渲染。

垃圾清理已接入分类扫描、类目/文件勾选、清理后果说明、排除目录、确认执行和结果明细。支持临时文件、诊断文件、图形缓存、标准浏览器缓存和回收站；扫描后默认全部不选。文件整理与软件搬家仍标注规划中。具体规则及限制见 [垃圾清理说明](tools/garbage-cleaner/README.md)。

当前支持声明式内容模块，以及随平台编译分发的可信内置垃圾清理模块。垃圾清理的安装按钮离线启用模块，尚未实现原生模块独立下载或热更新。通用原生子应用运行器、跨进程 IPC、自动更新回退、选择预装的正式安装器仍在 todo 中。`IPlatformContext` 当前是可信进程内契约，不构成不可信插件沙箱。

## 开发环境

- Windows、.NET SDK 10.0.400（参见 `global.json`）。无需 Node 构建，管理页面为原生 HTML/CSS/JavaScript。
- MySQL 8.4 服务；建库、账号授权和连接配置见 [数据库说明](backend/Database/README.md)。数据库只部署在后台，普通桌面用户无需安装 MySQL。
- 桌面运行需要 .NET 10 Desktop Runtime；当前生成的是依赖运行时的开发版本。
- 兼容目标为 Windows 10 22H2 x64 与 Windows 11 x64；当前仅在本机验证，尚未完成独立 Windows 10/11 测试机验收。
- .NET 的官方支持范围受 Windows 生命周期影响，Windows 10 的运行兼容目标与微软官方维护支持范围要分别记录。[微软 .NET Windows 支持说明](https://learn.microsoft.com/en-us/dotnet/core/install/windows)

## 构建和启动

在项目根目录的 PowerShell 中执行：

```powershell
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_CLI_HOME = Join-Path $PWD '.dotnet'
dotnet restore XieBox.sln -m:1
dotnet build XieBox.sln --no-restore -m:1
# 本机已安装项目专用 MySQL，启动脚本会自动启动数据库并载入加密凭据。
.\scripts\Start-Dev.ps1
# 安装并打开垃圾清理，不自动扫描或清理。
.\scripts\Start-Dev.ps1 -OpenCleaner
```

启动脚本启动本地后台并打开桌面平台；收起到托盘时桌面与后台继续运行，从托盘退出后停止该脚本创建的后台进程。桌面已运行时再次执行脚本会恢复原窗口，不重复构建占用中的程序或启动后台。已有后台时使用 `-DesktopOnly`。也可以分别启动：

```powershell
# 终端一
.\scripts\Start-Dev.ps1 -BackendOnly
# 终端二
.\scripts\Start-Dev.ps1 -DesktopOnly
```

开发启动脚本使用本地后台 `http://127.0.0.1:5188`，管理页为 `http://127.0.0.1:5188/admin/`。直接启动桌面程序及单文件便携版默认连接 `https://www.xie-bro.top/xie-bro-tool-box/`。普通账号不能进入管理后台；管理员自己的桌面会话也不能用于管理接口。

桌面端后台地址配置在 `platformsettings.json`，可通过 `XIEBOX_API_URL` 环境变量覆盖。HTTP 只允许回环地址，其他地址须使用 HTTPS。

## 本地注册与管理员

账号窗口合并登录与注册：输入邮箱和密码后，已有账号校验密码，新邮箱发送验证码，验证后注册并登录。密码错误不会触发注册。找回密码单独保留，旧用户名仍可登录。免费本地工具不要求登录。

注册和找回密码使用 6 位邮件验证码，有效期 5 分钟；重新发送后旧验证码失效，验证码只能使用一次。发送频率和配额由后台限制。SMTP 凭据只保存在服务端，客户端不包含邮箱密码。

个人中心显示邮箱和固定的 8 位 UID（大写字母与数字），不提供昵称或头像。UID 由服务器随机生成，数据库唯一索引防止重复；现有账号首次验证会话时补齐。UID 只用于识别账号，不能作为登录凭据或授权依据。

登录默认不勾选自动登录，凭据仅留在进程内；勾选后用 Windows DPAPI（当前用户）加密保存会话，不保存密码。取消勾选会立即删除本机凭据，当前窗口仍保持登录。访问凭据有效 20 分钟，刷新凭据轮换，整次登录固定最长 7 天，刷新不延长截止日期；到期需重新登录。旧服务签发的会话保留原有截止时间。关闭到托盘不等于退出应用。退出登录撤销当前服务端会话并删除本机凭据；断网时仍清除本机凭据，服务端撤销需联网成功。重置密码撤销所有旧会话。服务端密码使用 Identity 加盐哈希，数据库仅保存会话令牌的哈希。

账号认证、工具权益与 Windows 管理员权限独立判断。免费工具可离线使用；登录不等于购买 Pro，也不授予管理员权限。受限服务的权限必须由服务端验证，客户端不能仅依据按钮状态或 UID 放行。

本机已按要求创建开发管理员 `admin / admin123`，可在桌面端和管理后台登录。其他开发机器可在构建后执行 `.\scripts\Create-DevAdmin.ps1` 显式创建相同测试账号；生产模式拒绝此初始化命令，不自动生成默认账号。初始化使用自选密码的管理员：

```powershell
.\scripts\Bootstrap-Admin.ps1 -Username 'your-admin-name'
```

脚本以安全输入读取密码，通过短期进程环境变量传入初始化命令，不把密码写进源码或命令参数。随后在管理页面登录。

真实部署需配置 HTTPS、SMTP、服务端数据目录访问权限与备份，使用独立管理员凭据。生产模式拒绝非 HTTPS 请求；反向代理部署需要明确可信代理与转发头配置。

## 数据与源码位置

```text
根目录                         WPF 平台工程、界面、状态与本地服务
Contracts/                     平台用户与模块协议 DTO、IPlatformContext
backend/Api/                   API、Identity、服务端会话与访问控制
backend/Admin/                 管理后台静态页面
backend/Database/              数据库迁移与结构说明
tools/garbage-cleaner/          垃圾清理独立开发目录
tests/                         平台可执行测试与页面渲染
backend/tests/                 HTTP + MySQL 集成测试
scripts/                       本地启动、管理员初始化脚本
```

桌面偏好、目录缓存、已安装模块及 DPAPI 会话保存在 `%LOCALAPPDATA%/XieBox/`，可用 `XIEBOX_DATA_DIR` 指定独立测试目录。账号、会话、模块发布信息和审计记录保存在 MySQL，通过 `ConnectionStrings__PlatformDatabase` 配置连接。后台 `backend/Api/App_Data/` 保存数据保护密钥，用 `Platform__DataDirectory` 覆盖；历史开发邮件保留但不再使用。客户端不直接访问服务端数据库。

安装模块只写入平台数据目录，不覆盖 `tools/` 源码目录。卸载保留导航设置；垃圾清理另保留排除目录与结果摘要，摘要不保存完整文件路径或文件内容。扫描/清理任务中禁止卸载，收起窗口保留任务；从托盘退出时先请求取消，结束后退出。

## 模块包

内容模块包为 ZIP，根部含以下文件：

```text
module.json
page.json
```

`module.json`：

```json
{
  "id": "example-module",
  "name": "示例模块",
  "version": "0.1.0",
  "runtime": "content",
  "entry": "page.json",
  "protocolVersion": 1
}
```

`page.json`：

```json
{
  "title": "模块标题",
  "description": "模块介绍",
  "tips": ["第一条说明", "第二条说明"]
}
```

通过管理页发布模块 ID、版本、HTTPS 下载地址与审核后的 SHA-256。开发环境允许本机回环 HTTP。客户端刷新应用目录后可下载、校验、安装、打开及卸载。首版不执行模块包内脚本或程序，也不自动跟随下载重定向；为原生模块扩展运行器时必须另外落实发布者校验与权限边界。

SHA-256 验证依赖平台配置的 HTTPS 服务端发布信息，包自身提供的哈希不能成为信任依据。当前首版尚未完成生产签名发布体系。

## 验证

新增内置应用：[启动项管理](tools/startup-manager/README.md) 与 [内存释放](tools/memory-release/README.md)。两者在正常开发版首页显示，可离线安装；上一版两应用演示包仍保持垃圾清理和文件扫描的范围。启动项公共范围暂只读，内存整理只针对当前用户后台进程。

先完成构建，再执行：

```powershell
dotnet tests/bin/Debug/net10.0-windows/XieBox.Platform.Tests.dll
dotnet tools/garbage-cleaner/tests/bin/Debug/net10.0-windows/XieBox.GarbageCleaner.Tests.dll
# 使用项目本地 MySQL，自动创建新的独立测试库和测试账号。
.\scripts\Test-MySql.ps1
node --check backend/Admin/app.js
```

集成测试要求提供空的 `xiebox_test_*` 数据库，拒绝普通库名与已有数据；创建测试账号，仅监听随机本机端口，重启后台验证持久化，退出时停止自己启动的后台。测试不会自动删库，运行后数据保留供检查。密码和会话凭证不输出到终端。运行需要 MySQL 连接和本机 HTTP 访问。

本机已安装 MySQL 8.4.11，初始化 `xiebox` 开发库，完成编译、41 项平台集成检查及 35 项真实 MySQL API/启动检查，覆盖用户名统一登录注册、6 位密码边界、中文用户名、默认开发管理员、权限、模块安装和 API 重启后的数据持久化。垃圾清理另有受控测试，破坏性测试只操作新建样本，真实目录只读扫描不删除文件。本地 MySQL 仅监听 `127.0.0.1:3306`，通过启动脚本管理，不注册系统服务或开机自启。

WPF 渲染预览保存在 `artifacts/previews/`。这属于页面构建与布局检查，不能替代真实桌面交互、高 DPI 或 Windows 10 虚拟机测试。

新增托盘生命周期与重复启动验证后，当前离线平台测试共 39 项通过，覆盖关闭隐藏、页面保留、最大化恢复、后台任务继续、等待取消后退出及实例激活。托盘图标采用 Windows 通知区接口，Windows 可能将新图标放入右下角折叠区。

已确认需求和剩余工作参见 [平台 todoList](todoList.md)、[架构文档](docs/architecture.md) 和 [垃圾清理 todoList](tools/garbage-cleaner/todoList.md)。
