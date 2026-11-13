namespace AutoAddSchedule.Services;

public enum NotificationKind
{
    Info = 0,
    Warning = 1,
    Error = 2
}

public interface INotificationService
{
    /// <summary>弹出面向用户的通知（托盘气泡），可从任意线程调用。</summary>
    void Show(string title, string message, NotificationKind kind = NotificationKind.Info);
}
