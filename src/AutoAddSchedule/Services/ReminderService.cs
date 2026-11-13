using System.Windows.Threading;
using AutoAddSchedule.Core;

namespace AutoAddSchedule.Services;

/// <summary>每 20 秒检查一次日程提醒时间，到点弹托盘通知并记录已提醒。</summary>
public sealed class ReminderService : IReminderService
{
    private static readonly TimeSpan LateWindow = TimeSpan.FromMinutes(5);
    private readonly DispatcherTimer _timer;
    private bool _started;

    public ReminderService()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(20)
        };
        _timer.Tick += (_, _) => CheckReminders();
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _timer.Start();
        CheckReminders();
    }

    public void Stop()
    {
        _timer.Stop();
        _started = false;
    }

    public void Dispose() => Stop();

    private void CheckReminders()
    {
        try
        {
            var now = DateTime.Now;
            foreach (var item in AppServices.Schedules.GetAll())
            {
                if (item.LastNotifiedAt is not null)
                {
                    continue;
                }

                var due = item.ReminderLocal;
                if (now < due || now > due + LateWindow)
                {
                    continue;
                }

                item.LastNotifiedAt = DateTime.Now;
                AppServices.Schedules.Update(item);
                AppServices.Notifications.Show("日程提醒", BuildMessage(item), NotificationKind.Info);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("检查日程提醒失败", ex);
        }
    }

    private static string BuildMessage(ScheduleItem item)
    {
        var text = item.Title + Environment.NewLine + TimeFormat.FormatDateTime(item.StartLocal);
        if (item.ReminderMinutesBefore > 0)
        {
            text += "（提前 " + item.ReminderMinutesBefore + " 分钟）";
        }

        if (!string.IsNullOrWhiteSpace(item.Location))
        {
            text += Environment.NewLine + item.Location;
        }

        return text;
    }
}
