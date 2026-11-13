using AutoAddSchedule.Core;

namespace AutoAddSchedule.Services;

/// <summary>
/// 所有服务的组合根。由 App.OnStartup 初始化，界面与服务统一从这里取实例。
/// </summary>
public static class AppServices
{
    public static ConfigStore Config { get; private set; } = null!;

    public static ScheduleStore Schedules { get; private set; } = null!;

    public static ILlmClient Llm { get; private set; } = null!;

    public static IScheduleExtractor Extractor { get; private set; } = null!;

    public static ICalendarBuilder CalendarBuilder { get; private set; } = null!;

    public static ICalDavClient CalDav { get; private set; } = null!;

    public static ISyncService Sync { get; private set; } = null!;

    public static IScreenshotStore Screenshots { get; private set; } = null!;

    public static ICaptureOverlayService CaptureOverlay { get; private set; } = null!;

    public static IHotkeyService Hotkey { get; private set; } = null!;

    public static ITrayService Tray { get; private set; } = null!;

    public static INotificationService Notifications { get; private set; } = null!;

    public static IAutoStartService AutoStart { get; private set; } = null!;

    public static IReminderService Reminders { get; private set; } = null!;

    public static CaptureController Capture { get; private set; } = null!;

    public static void Initialize()
    {
        Config = new ConfigStore();
        Config.Load();

        Schedules = new ScheduleStore();
        Schedules.Load();

        Llm = new LlmClient();
        Extractor = new ScheduleExtractionService();
        CalendarBuilder = new CalendarBuilder();
        CalDav = new CalDavClient();
        Sync = new SyncService();
        Screenshots = new ScreenshotStore();
        CaptureOverlay = new CaptureOverlayService();
        Hotkey = new HotkeyService();
        Tray = new TrayService();
        Notifications = new NotificationService();
        AutoStart = new AutoStartService();
        Reminders = new ReminderService();
        Capture = new CaptureController();

        AppLog.Info("服务初始化完成。");
    }

    public static void Shutdown()
    {
        try
        {
            Reminders?.Dispose();
            Hotkey?.Dispose();
            Tray?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Error("关闭服务时出错", ex);
        }
    }
}
