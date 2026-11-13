using AutoAddSchedule.Core;

namespace AutoAddSchedule.Services;

/// <summary>把通知统一转成托盘气泡，并保证在 UI 线程执行。</summary>
public sealed class NotificationService : INotificationService
{
    public void Show(string title, string message, NotificationKind kind = NotificationKind.Info)
    {
        AppLog.Info("通知：" + title + " | " + message.Replace(Environment.NewLine, " / "));
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            return;
        }

        var balloonIcon = kind switch
        {
            NotificationKind.Warning => BalloonIcon.Warning,
            NotificationKind.Error => BalloonIcon.Error,
            _ => BalloonIcon.Info
        };

        app.Dispatcher.BeginInvoke(() => AppServices.Tray.ShowBalloon(title, message, balloonIcon));
    }
}
