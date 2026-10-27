# 启动项管理

离线内置模块 `startup-manager`，沿用平台身份与免费能力 `scan` / `manage`。入口采用居中介绍，读取后过渡至可筛选列表；详情和确认共用平台弹窗、拖拽标题栏与淡入淡出。

## 首版范围

- 查看当前用户 Run、所有用户 64/32 位 Run，以及当前用户和公共启动文件夹。
- 当前用户项目可关闭和恢复。所有用户项目仅展示，提供 Windows 启动应用设置入口。
- 尊重 Windows StartupApproved 中已有的禁用状态，此类项目交由系统设置恢复，不写入该未公开约定的结构。
- 不处理 RunOnce、服务、驱动、计划任务、商店应用启动任务；不把列表称作系统全部启动机制。
- 发布者取自可识别本地程序的版本信息，明确标注“文件声明”，不等同于签名验证。无法识别的用途不做推荐。

## 恢复与冲突

注册表关闭时，原始值和类型先写入同级 `Run.XieBoxDisabled` 键并刷新落盘，再删除 Run 原值。恢复执行反向步骤，拒绝覆盖同名值。若中途失败，可能同时保留两份；会显示失败并要求刷新，原始数据不会被静默覆盖。备份不随模块卸载删除，重新安装后仍可恢复。

启动文件夹中的文件移到其 `XieBox.DisabledStartup` 子目录，恢复时移回；不覆盖同名文件，不处理链接目录和重解析文件。关闭只影响该条自动启动来源，不保证应用不存在其他自启动机制。

测试使用独立 `HKCU\Software\XieBox.TestFixtures\随机ID` 和项目 artifacts 下的启动文件夹样本，最后只清除自己创建的测试注册表项，不改真实启动项。

参考：[微软 Run / RunOnce 文档](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)、[Windows 启动应用设置](https://support.microsoft.com/en-US/Windows/Experience/Startup-Boot/configure-startup-applications-in-windows)。
