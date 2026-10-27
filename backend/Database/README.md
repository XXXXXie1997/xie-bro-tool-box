# 平台数据库：MySQL

## 邮箱注册与密码找回（2026-10-16）

新账号以邮箱为登录账号，注册填写邮箱、密码及 6 位验证码；验证码校验通过后才创建用户并登录。普通登录仍使用账号密码。旧用户名账号可以继续登录，未绑定已验证邮箱的旧账号不能通过邮件找回密码。

新增 `AddEmailChallenges` 迁移创建验证码表，并将 `AspNetUsers.NormalizedEmail` 索引改成唯一索引。升级前先检查已有非空邮箱是否重复并备份数据库，不会自动合并或删除用户。`PendingRegistrations` 保留为历史表。

验证码有效期 5 分钟，输错不会使验证码提前失效，可在有效期内重试；成功使用或重新发送后旧验证码失效。账号接口仍保留每 IP 每分钟 20 次请求的频率限制。同邮箱发送间隔至少 60 秒，滚动 24 小时最多 5 次。全局默认每天最多 100 次，可以用 `Mail:DailyLimit` 设置。发送失败也计入限额。验证码加密保存，数据保护密钥应与数据库一起妥善备份。MySQL 数据库级命名锁串行保护发送限额和一次性消费；重置密码会撤销用户全部会话。注册和找回密码邮件的主题及正文首行均以“【蟹小箱】”开头并紧接验证码，方便邮箱通知展示。

邮件使用 MailKit 连接 `smtp.qq.com:465`，要求 TLS 证书校验通过。发件人是 `tianyu.xie0727@qq.com`，授权码只通过 `Mail__Password` 或服务器受限配置注入，不写入代码或客户端。Production 可通过 `XIEBOX_SECRETS_FILE` 加载服务器 JSON 配置。显式 `Mail__Mode=DevelopmentFile` 只允许 Development 使用，将测试邮件写到数据目录下的 `dev-mail`，不会向公网发信。

接口为 `/api/v1/auth/register`（email/password）、`verify`（challengeId/code）、`forgot-password`（email）和 `reset-password`（challengeId/code/newPassword）。旧 `/continue` 仅登录，不再自动注册。Production 启动不会自动执行迁移，需先以部署权限执行 `--migrate`，之后运行账号只保留该数据库的读写权限。

服务器首次部署脚本在 `scripts/server/deploy-api.sh`，需要经过检查的 `api/` 发布文件与 `files.sha256` 清单。运行目录为 `/opt/xiebox/current`，服务为 `xiebox-api`，私有数据位于 `/var/lib/xiebox`，配置位于 `/etc/xiebox/api.json`。Nginx 只代理官网路径下的 `/api/` 和 `/health`，不会公开后端管理页面。`--test-mail` 是运维命令，只应在获得收件人测试授权后手动运行。

后端发布命令：`dotnet publish backend/Api/XieBox.Api.csproj -c Release -r linux-x64 --self-contained true -p:RuntimeFrameworkVersion=10.0.13 -p:ServerGarbageCollection=false -o artifacts/server/xiebox-api`。桌面默认连接官网同路径，`Start-Dev.ps1` 使用本地 API。

开发与生产后台统一使用 MySQL，目标版本为 MySQL 8.4。通过 Oracle 官方 `MySql.EntityFrameworkCore` 10.0.9 接入 EF Core 10；当前 MySQL 初始迁移在 `Migrations/`，API 工程链接编译这些文件。[驱动包信息](https://www.nuget.org/packages/MySql.EntityFrameworkCore/10.0.9)

桌面端和子应用只调用平台 API，不持有数据库凭据。普通用户无需安装 MySQL，免费本地功能仍可离线使用。

## 建库与连接

本机已安装官方 MySQL 8.4.11 ZIP 版本，程序在 `.local/mysql-8.4.11-winx64/`，数据、日志及配置在 `.local/mysql/`，开发库名为 `xiebox`。仅监听 `127.0.0.1:3306`，关闭 X Protocol，连接要求 TLS，不注册 Windows 服务、不随开机启动。

日常在项目根目录运行 `.\scripts\Start-Dev.ps1` 即可自动启动本地数据库并载入开发连接。只管理数据库时使用：

```powershell
.\scripts\Start-LocalMySql.ps1
.\scripts\Stop-LocalMySql.ps1
```

root 和开发账号密码均随机生成，保存在 `.local/mysql/accounts.xml`，密码使用 Windows DPAPI 加密，只有当前 Windows 用户在本机可以解密。数据目录 ACL 仅允许当前用户与 SYSTEM；不要提交 `.local/`，不要删除 `.local/mysql/data/`。开发连接只获得 `xiebox` 库权限。需要连接其他 MySQL 实例时，用下面的手动配置方式覆盖环境变量。

在其他开发机器手动安装时：

先准备运行中的 MySQL 服务。数据库管理员执行 `create-database.sql`，创建 `xiebox` 库，字符集为 `utf8mb4`、排序规则为 `utf8mb4_bin`。后台在迁移前检查这两项，保证建表时继承正确默认值。为后台创建独立数据库用户，不使用 root；本地开发启动会执行迁移，因此该用户需要此库内的建表、索引、变更表及读写权限。用户名和密码由部署环境设置。

在项目根目录同一个 PowerShell 终端中配置并启动：

```powershell
.\scripts\Set-MySqlConnection.ps1 -Server 127.0.0.1 -Database xiebox -User xiebox -SslMode Preferred
.\scripts\Start-Dev.ps1
# 同一个终端也可运行管理员初始化：
.\scripts\Bootstrap-Admin.ps1 -Username 'your-admin-name'
```

脚本安全读取密码，通过连接串构建器处理特殊字符，只设置当前进程的 `ConnectionStrings__PlatformDatabase` 环境变量，不输出或保存密码。服务部署时通过秘密配置提供同名环境变量；格式为 `Server=主机;Port=3306;Database=xiebox;User ID=用户名;Password=密码;SslMode=VerifyFull`。远程连接应配置可信 CA 和主机名验证。`Preferred` 只用于本机开发，可能退回未加密连接。

未提供连接字符串时 API 会明确报错；不会创建 SQLite 或自动连接默认数据库。API 启动迁移不会自动创建数据库用户。现有 `App_Data/platform.db` 不会被读取、删除或转换；旧原型数据如需保留，需要单独迁移方案。后台数据目录继续保存开发邮件和数据保护密钥。

## 数据表

- Identity 用户、角色、用户角色、声明及相关认证表。
- `PendingRegistrations`：历史注册流程遗留表；保留迁移与数据，新流程不再使用它。
- `EmailChallenges`：邮箱注册及密码找回的验证码挑战，保存加密验证码、有效期、尝试次数和消费状态；最近两天记录用于持久化发送限额。
- `Sessions`：用户 ID、安全戳快照、访问/刷新凭证哈希、有效期、会话用途与撤销状态。
- `ConsumedRefreshes`：已消费的刷新凭证哈希，识别重放并撤销对应会话。
- `Modules`：当前模块发布描述与更新时间；历史版本表待实现。
- `Audit`：管理员初始化、用户状态变更、会话撤销、模块发布与停用记录。

密码由 Identity 哈希；会话凭证只存 SHA-256 哈希。主键、外键与索引字符串明确限制长度，避免 MySQL 多字节字符集索引超长。管理员会话要求 `Admin` 角色及 `admin` 用途。

## 维护迁移

在项目根目录执行：

```powershell
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_CLI_HOME = Join-Path $PWD '.dotnet'
dotnet tool install dotnet-ef --tool-path .local/tools --version 10.0.11
dotnet build backend/Api/XieBox.Api.csproj --no-restore -m:1
.\.local\tools\dotnet-ef.exe migrations add YourMigrationName --project backend/Api/XieBox.Api.csproj --startup-project backend/Api/XieBox.Api.csproj --output-dir ../Database/Migrations --namespace XieBox.Api.Migrations --no-build
.\.local\tools\dotnet-ef.exe migrations script --project backend/Api/XieBox.Api.csproj --startup-project backend/Api/XieBox.Api.csproj --no-build --output artifacts/mysql-migrations.sql
```

已安装工具时跳过安装步骤；增加迁移后重新构建再生成 SQL。以上脚本从空库开始，仅适用于新库；升级现有库时在 `migrations script` 后明确指定起止迁移，不重复执行建表 SQL。当前驱动的 `--idempotent` 输出含 MySQL 顶层不支持的条件语句，不用于部署。设计时工厂生成模型和 SQL 不连接服务器；执行数据库更新需先配置真实连接。生成迁移时 EF 工具可能把模型快照放在 API 的命名空间目录，请将其移到 `backend/Database/Migrations/` 后重新构建，确保所有迁移文件集中维护。

本地 API 启动时应用迁移。正式部署前仍需落实独立迁移发布流程、部署账号最小权限、备份恢复演练、密钥存储权限、过期会话清理和升级测试。

## 集成测试

本机可直接运行 `.\scripts\Test-MySql.ps1`，自动创建新的独立测试库和仅能访问该库的临时账号，再运行 Python 集成测试。数据库和测试账号保留供检查，供后续人工清理；不使用 root 运行 API。需要 Python、已构建的解决方案和本机网络访问权限。

使用其他 MySQL 实例手动测试时：

由数据库管理员创建新的空库，例如 `xiebox_test_local_001`，使用相同字符集和排序规则，为测试用户授予该库内的迁移和读写权限。测试账号与开发、生产账号分开，每次运行使用一个新库。

```powershell
.\scripts\Set-MySqlConnection.ps1 -Database xiebox_test_local_001 -User xiebox_test -SslMode Preferred -ForIntegrationTest
python backend/tests/integration.py
```

测试启动前检查库名必须以 `xiebox_test_` 开头且没有任何表；不接受已有数据、不自动删库。测试涵盖注册、验证、登录、会话轮换、权限隔离、管理发布、桌面模块安装和 API 重启后的持久化。日志及开发邮件放在 `artifacts/api-tests/`，数据库保留供人工检查。

2026-10-16 已完成邮箱账号流程联调：43 项后端集成检查、9 项桌面账号检查及应用启动检查通过，覆盖两种邮件模板的验证码位置、连续输错后仍可验证、真实等待 5 分钟后的到期拒绝、邮箱验证码注册、发送冷却、重复使用、密码重置和旧会话撤销。登录、注册及找回密码窗口另通过 6 项界面检查。已有用户 ID 保留，旧用户名仍可登录；未绑定已验证邮箱的旧账号不能通过邮件找回密码。
