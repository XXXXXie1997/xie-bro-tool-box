# 同类应用与规则依据

调研日期：2026-10-12。以下依据为厂商官方页面或官方帮助；没有把厂商宣传中的提速承诺作为本项目验收指标，也没有复制其源码规则。

| 应用 / 来源 | 查到的做法 | 蟹小箱采用的决策 |
| --- | --- | --- |
| [微软电脑管家](https://pcmanager.microsoft.com/zh-cn) | 提供空间管理、深度清理与大文件管理 | 首版聚焦可解释的空间回收，大文件管理另行规划 |
| [Windows 清理帮助](https://support.microsoft.com/en-us/windows/experience/storage-filemanagement/free-up-drive-space-in-windows) | 用户选择存储清理范围，回收站文件仍占空间 | 候选大小、成功处理逻辑大小和磁盘可用空间变化分别表达 |
| [360 清理说明](https://www.360.cn/privacy/v2/360anquanweishi.html) | 按用户范围扫描特定位置，结果列出后用户选择清理 | 按类目勾选，扫描与执行分开；不照搬注册表或全盘类型清理 |
| [火绒个人产品](https://www.huorong.cn/person) | 包括垃圾清理功能，强调简洁和不打扰 | 保持单一任务界面，无健康分数、夸张警报或强制账号 |
| [CCleaner Custom Clean](https://support.ccleaner.com/s/article/custom-clean-s-analysis-and-results-function?language=en_US) | 分应用显示文件数量、大小，可进入文件明细 | 类目旁直接写影响，支持查看候选路径与逐项保留 |
| [CCleaner Cookie 说明](https://support.ccleaner.com/articles/en_US/Master_Article/select-cookies-to-clean-with-ccleaner-for-windows) | Cookie 可能保存登录、购物车和偏好等状态 | 当前不提供 Cookie 清理，缓存与用户状态严格分离 |
| [BleachBit 文档](https://docs.bleachbit.org/index.html) | 选择清理项后先预览，再执行清理 | 默认不选择任何类目，必须扫描、勾选并确认 |
| [Wise Disk Cleaner Advanced Cleaner](https://www.wisecleaner.com/help/wisediskcleaner/how-to-use/advanced-cleaner.html) | 扫描结果可排序和排除，提醒某些文件是否可删需用户判断 | 允许排除目录和取消单文件；首版不提供全盘扩展名删除 |

技术依据：

- [Chromium User Data Directory](https://chromium.googlesource.com/chromium/src.git/+/HEAD/docs/user_data_dir.md)：Windows 用户配置路径与自定义位置。配置目录不是可整体删除的缓存目录。
- [Firefox 缓存帮助](https://support.mozilla.org/en-US/kb/how-clear-firefox-cache)：网页缓存与 Cookie、网站数据分别选择，缓存可重新生成。
- [360 官方缓存说明](https://browser.360.cn/se/help/faq-detail_jmxswt_jmxswt.html)：其具体缓存布局有版本差异，因此只识别明确缓存子目录，不删除 User Data。
- [CreateFileW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew)、[SetFileInformationByHandle](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-setfileinformationbyhandle)：目录句柄、共享模式、不跟随重解析点和通过句柄删除。
- [SHQueryRecycleBinW](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shqueryrecyclebinw)、[SHEmptyRecycleBinW](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shemptyrecyclebinw)：查询和清空当前用户回收站，保留系统确认。

本项目独立作出的保守选择：过期临时文件限定创建和修改均超过 7 天，并只匹配 .tmp、.temp、.log；崩溃报告超过 7 天、着色器缓存超过 1 天。以上是首版产品规则，不是对所有软件临时资料都可以安全删除的保证，所以类目提示仍要求用户按需选择。
