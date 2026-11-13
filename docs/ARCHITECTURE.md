# 架构说明

## 总体结构

程序为单进程 WPF 应用，常驻系统托盘。核心流程：

```text
全局快捷键 / 托盘菜单
        ↓
截图选区界面（多显示器、高 DPI）
        ↓ 裁剪图片 + 补充信息
保存截图（程序目录 screenshots/，保留 60 张）
        ↓
大模型提取日程（DeepSeek，图片 + 补充信息 → JSON）
        ↓
结构化日程（标题/时间/地点/描述/提前量）→ 本地存储（%AppData%）
        ↓
iCalendar VEVENT 生成 → iCloud CalDAV 同步
        ↓
托盘通知结果；日程到点前按提前量提醒
```

## 分层

- Core/：与界面无关的基础设施。路径（AppPaths）、配置（AppConfig / ConfigStore）、
  密钥保护（SecretProtector，DPAPI）、数据模型（ScheduleItem）、本地日程存储（ScheduleStore）、
  时间格式化（TimeFormat）、日志（AppLog）、快捷键模型（HotkeyGesture）。
- Services/：业务服务，全部通过接口装配（见 AppServices）。
- UI/：WPF 界面。MainWindow 为主窗口外壳（日历 / 日程列表 / 设置）；UI/Capture 为截图选区界面；
  UI/Dialogs 为编辑、补充信息等对话框。

## 服务清单

- ILlmClient / LlmClient：OpenAI 兼容的 /chat/completions 调用，支持图片输入。
- IScheduleExtractor / ScheduleExtractionService：提示词、JSON 输出约束、结果解析与校验。
- ICalendarBuilder / CalendarBuilder：按 RFC 5545 生成 VCALENDAR/VEVENT（含提醒 VALARM）。
- ICalDavClient / CalDavClient：iCloud CalDAV 连接、日历发现、事件创建/更新/删除。
- ISyncService / SyncService：本地日程与 iCloud 的同步与失败重试标记。
- ICaptureOverlayService / CaptureOverlayService：全屏截图选区交互（多显示器、高 DPI）。
- IScreenshotStore / ScreenshotStore：截图落盘与「保留最近 N 张」清理。
- IHotkeyService / HotkeyService：Win32 RegisterHotKey 全局快捷键。
- ITrayService / TrayService：WinForms NotifyIcon 托盘图标、菜单与气泡通知。
- IReminderService / ReminderService：日程提醒计时与去重。
- IAutoStartService / AutoStartService：开机自启（HKCU Run）。
- CaptureController：串联「截图 → 存图 → 提取 → 存储 → 同步 → 通知」。

## 存储位置

- 配置：%AppData%\AutoAddSchedule\config.json（不含密钥）
- 密钥：%AppData%\AutoAddSchedule\secrets.dat（DPAPI 加密）
- 日程：%AppData%\AutoAddSchedule\schedules.json
- 日志：%AppData%\AutoAddSchedule\logs\app.log
- 截图：程序所在目录 screenshots\

## 线程模型

界面在 UI 线程；截图、网络、同步均在后台线程异步执行。服务通过 Application.Current.Dispatcher
回到 UI 线程更新界面与弹通知。AppServices 是所有服务的组合根，由 App.OnStartup 初始化。
