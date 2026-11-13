# 主界面

主界面基于 WPF 原生控件与自绘实现，不使用第三方 UI 库。窗口标题为「AutoAddSchedule 自动添加日程」，
采用左侧深色导航 + 右侧内容区的布局。

## 窗口与导航

- 左侧导航固定三项：日历、日程、设置；当前页面对应的导航项显示蓝色强调条与高亮背景。
- 右侧内容区按需切换视图，切换时视图会自动重新订阅数据变更并刷新。
- 关闭窗口只是隐藏到系统托盘，不退出程序；真正退出只能通过托盘菜单或「设置 → 通用 → 退出程序」。
- 左侧底部显示当前截图快捷键与「托盘常驻」提示；保存设置后快捷键提示会自动刷新。
- 窗口默认 1120 × 820，最小 900 × 640；启动时按屏幕工作区自动收缩，并在工作区内居中，
  保证标题栏始终可见、可拖动（避免高 DPI / 小屏幕下窗口顶部跑到屏幕外导致无法移动）。

## 日历页（UI/Views/CalendarView）

自绘月视图，周一为第一天，固定 6 行 × 7 列：

- 顶部：「‹ / 2026年11月 / ›」月份导航与「今天」按钮。
- 星期表头：一 二 三 四 五 六 日。
- 日期格：显示日号，若有日程再显示最多 3 条「时间 + 标题」摘要，超出显示「+N」。
- 今天：日号使用蓝色圆形高亮。
- 非本月日期：日号与摘要使用浅灰显示；点击后自动跳转到该日期所在月份。
- 选中日期：日期格使用浅蓝底 + 蓝色边框；下方显示该日期的日程列表（空时显示「当天没有日程」）。
- 双击下方列表中的日程可直接打开编辑对话框。
- 「新增日程」按钮默认日期为当前选中日期。
- 数据变更（新增 / 编辑 / 删除 / 同步结果 / 大模型提取）通过 AppServices.Schedules.Changed 事件自动刷新。

## 日程页（UI/Views/ScheduleListView）

全部日程的列表视图，按开始时间升序排列，列包括：开始时间、标题、地点、提醒、同步状态。

- 同步状态：已同步（绿色）/ 未同步（橙色）/ 同步失败（红色）。
- 新增 / 编辑：打开日程编辑对话框，保存后写入本地；若已配置 iCloud 日历则自动尝试同步，并在状态栏显示结果。
- 删除：先二次确认，然后软删除本地日程并尝试删除 iCloud 上的事件；删除成功则从数据文件移除，
  删除失败则保留待同步标记，提示用户稍后可点击「刷新同步」重试。
- 刷新同步：调用 AppServices.Sync.PushPendingAsync() 推送全部未同步日程，完成后显示成功条数与仍未同步的条数。
- 所有网络操作期间禁用操作按钮，并在状态栏显示「正在同步…」等提示；异常以中文提示并写入日志。

## 设置页（UI/Views/SettingsView）

分为「大模型 / iCloud 日历 / 快捷键 / 通用」四张卡片，所有修改在点击底部「保存设置」后统一生效。
「保存设置」栏固定在页面底部，滚动卡片时始终可见。

### 大模型

- Base URL、模型名、API Key 三项；默认 https://api.deepseek.com 与 deepseek-flash。
- 说明文字提醒：默认 deepseek-flash 支持图片输入；deepseek-v4-pro 不支持图片，不能用于截图识别。
- API Key 为 PasswordBox：已保存时显示「已保存（留空表示不修改）」占位提示，输入新值才会覆盖。
- 「测试连接」异步调用 AppServices.Llm.TestConnectionAsync()，在按钮右侧显示成功（含模型信息）或中文失败原因。

### iCloud 日历

- Apple ID、应用专用密码（PasswordBox，规则同上）。
- 「测试连接」异步调用 AppServices.CalDav.TestConnectionAsync()。
- 「读取日历列表」异步调用 AppServices.CalDav.GetCalendarsAsync()，把日历填入下拉框（显示 DisplayName）；
  选中后点击「保存设置」，将日历 URL 写入 CalDav.CalendarHref、显示名写入 CalDav.CalendarDisplayName。
- 测试连接 / 读取日历使用界面当前输入的值（未保存也能测试），操作结束后恢复原配置，避免未保存内容被意外持久化。

### 快捷键

- 输入框只读，点击聚焦后按组合键即可捕获（PreviewKeyDown + HotkeyGesture.FromWpfKey）。
- 至少需要一个修饰键（Ctrl / Alt / Shift / Win），不满足时给出中文提示。
- 按 Backspace（或 Delete）恢复默认快捷键 Ctrl+Alt+\。
- 保存后若 AppServices.Hotkey.IsRegistered 为 false，页面显示注册失败原因，提示更换快捷键。

### 通用

- 开机自启：开关状态写入 General.AutoStart，保存时由 App.ApplySettings() 应用到注册表启动项。
- 启动后最小化到托盘：写入 General.StartMinimizedToTray。
- 截图保留数量：1-1000 的整数，写入 General.ScreenshotRetention，合法性与范围在保存时校验。
- 「打开截图目录」在资源管理器中打开程序目录下的 screenshots 文件夹。
- 「退出程序」二次确认后调用 App.ExitApplication() 退出。

保存设置会调用 App.ApplySettings()：持久化配置、重新注册快捷键、应用开机自启、刷新托盘提示，
并清空密码输入框（改回「已保存」占位状态）。

## 日程编辑对话框（UI/Dialogs/ScheduleEditDialog）

新增与编辑共用，构造函数为 ScheduleEditDialog(ScheduleItem? item = null, DateTime? defaultDate = null)：

- 字段：标题（必填）、日期（DatePicker）、开始时间（HH:mm）、结束时间（可选 HH:mm）、地点、描述、
  提前提醒分钟数（0 表示不提醒，可为空）、全天复选框。
- 新增：默认日期为调用方传入的日期（日历页为选中日期，日程页为今天）；默认开始时间为今天下一个整/半点或其它日期的 09:00，
  默认提前提醒 15 分钟。
- 编辑：打开时回填原日程全部字段；确定时复制原对象后只覆盖界面字段（含 Uid / ETag 等同步字段），
  因此取消不会改动内存中的原日程。
- 校验：标题必填；时间必须为 HH:mm（也兼容 9:30 这类写法）；结束时间不得早于开始时间；
  提前提醒必须为 0-10080 的整数。校验失败在对话框内显示中文提示并聚焦对应输入框。
- 全天：勾选后禁用时间输入（开始时间按 00:00 保存，不保存结束时间）。
- 确定后 Result 为待保存日程对象，取消返回 null（DialogResult 为 false）。
- 编辑保存时若开始时间或提醒提前量发生变化，会重置 LastNotifiedAt，保证到点提醒可以重新触发。

## 与服务层的交互

- 界面统一通过 AppServices 取服务实例，不直接创建网络客户端。
- 日程数据通过 AppServices.Schedules（GetAll / GetForDay / Find / Add / Update / MarkDeleted / RemoveHard）读写；
  数据变更由 ScheduleStore.Changed 事件驱动界面刷新。
- 新增 / 编辑后自动同步仅在已配置 iCloud 日历（CalDav.IsConfigured 且已选择 CalendarHref）时触发；
  未配置时静默保留为「未同步」，不打断用户操作。
- 「刷新同步」与删除远端事件均通过 AppServices.Sync 完成，界面只负责状态展示与重试提示。

## 异常与线程

- 所有网络操作 async/await，不阻塞 UI 线程；操作期间禁用相关按钮。
- 数据变更事件统一通过 Dispatcher 回到 UI 线程刷新。
- 网络或同步异常以中文提示（状态栏 / 对话框 / 托盘通知），并通过 AppLog 写入
  %AppData%\AutoAddSchedule\logs\app.log。
- 界面不会显示或记录 API Key、应用专用密码等密钥内容。