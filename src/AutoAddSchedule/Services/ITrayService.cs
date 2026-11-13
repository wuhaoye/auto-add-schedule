namespace AutoAddSchedule.Services;

public enum BalloonIcon
{
    Info = 0,
    Warning = 1,
    Error = 2
}

public interface ITrayService : IDisposable
{
    event EventHandler? OpenRequested;

    event EventHandler? CaptureRequested;

    event EventHandler? SettingsRequested;

    event EventHandler? ExitRequested;

    /// <summary>显示托盘图标（程序启动时调用一次）。</summary>
    void Show();

    /// <summary>弹出托盘气泡通知。</summary>
    void ShowBalloon(string title, string message, BalloonIcon icon = BalloonIcon.Info);

    void UpdateTooltip(string text);
}
