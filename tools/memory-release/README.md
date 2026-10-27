# 内存释放

离线内置模块 `memory-release`，通过平台免费能力 `release` 授权。页面可见时每 2 秒读取一次物理内存数据；离开页面或最小化 / 隐藏后停止轮询。

## 行为

- `GlobalMemoryStatusEx` 展示物理内存总量、已用、可用和占用比例。
- 用户主动点击时，对当前用户、当前会话、无主窗口且工作集大于 16 MiB 的后台进程尝试 `K32EmptyWorkingSet`。
- 通过进程令牌 SID 校验用户，排除宿主、前台进程、关键进程、不可访问及已退出进程；执行前再次检查窗口和前台状态。
- 只申请查询和工作集整理权限，不结束进程、不清空待机列表、不提权、不进行定时释放。
- 工作在后台任务中，可随平台退出取消；页面不显示虚构百分比进度。
- 结果展示全系统可用内存前后差值，不将差值完全归因于操作；差值为零或负时如实显示暂无明显增加。

工作集页面可能被应用重新访问，整理后再次使用应用可能短暂变慢。可用内存增多不等于运行速度提高。

测试仅整理独立创建的 128 MiB 后台测试子进程，确认工作集下降且进程仍运行；不在测试中整理真实用户应用。模型结果页使用固定数据验证正、零、负差值等状态。

参考：[EmptyWorkingSet](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-emptyworkingset)、[Working Set](https://learn.microsoft.com/en-us/windows/win32/memory/working-set)。
