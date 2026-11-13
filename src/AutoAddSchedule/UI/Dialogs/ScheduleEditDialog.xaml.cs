using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoAddSchedule.Core;
using AutoAddSchedule.Services;

namespace AutoAddSchedule.UI.Dialogs;

/// <summary>
/// 新增 / 编辑日程对话框。字段：标题、日期、开始时间、结束时间、地点、描述、提前提醒、全天。
/// 点击「确定」后 <see cref="Result"/> 为待保存的日程对象；取消为 null。
/// </summary>
public partial class ScheduleEditDialog : Window
{
    private const int MaxReminderMinutes = 10080; // 最多提前一周

    private readonly ScheduleItem? _original;

    public ScheduleEditDialog(ScheduleItem? item = null, DateTime? defaultDate = null)
    {
        InitializeComponent();
        _original = item;
        LoadValues(item, defaultDate);
    }

    /// <summary>点击「确定」后生成的日程对象（编辑时为字段复制的新实例）；取消时保持 null。</summary>
    public ScheduleItem? Result { get; private set; }

    private void LoadValues(ScheduleItem? item, DateTime? defaultDate)
    {
        if (item is not null)
        {
            Title = "编辑日程";
            HeadingText.Text = "编辑日程";
            TitleBox.Text = item.Title;
            DateBox.SelectedDate = item.StartLocal.Date;
            AllDayCheckBox.IsChecked = item.AllDay;
            StartTimeBox.Text = item.StartLocal.ToString("HH:mm");
            if (item.EndLocal.HasValue)
            {
                EndTimeBox.Text = item.EndLocal.Value.ToString("HH:mm");
            }

            LocationBox.Text = item.Location ?? string.Empty;
            DescriptionBox.Text = item.Description ?? string.Empty;
            ReminderBox.Text = item.ReminderMinutesBefore.ToString();
        }
        else
        {
            Title = "新增日程";
            HeadingText.Text = "新增日程";
            var date = (defaultDate ?? DateTime.Today).Date;
            DateBox.SelectedDate = date;
            var start = DefaultStartTime(date);
            StartTimeBox.Text = start.ToString("HH:mm");
            EndTimeBox.Text = start.Add(TimeSpan.FromHours(1)).ToString("HH:mm");
            ReminderBox.Text = "15";
            AllDayCheckBox.IsChecked = false;
        }

        ApplyAllDayState();
        TitleBox.Focus();
    }

    /// <summary>新增时的默认开始时间：今天取下一个整/半点，其它日期取 09:00。</summary>
    private static DateTime DefaultStartTime(DateTime date)
    {
        if (date.Date != DateTime.Today)
        {
            return date.Date.AddHours(9);
        }

        var now = DateTime.Now;
        var minutes = now.Minute < 30 ? 30 : 60;
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddMinutes(minutes);
    }

    private void AllDayCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (AllDayCheckBox.IsChecked != true)
        {
            // 从全天切回指定时间时，补一个合理的默认时间。
            if (string.IsNullOrWhiteSpace(StartTimeBox.Text) || StartTimeBox.Text.Trim() == "00:00")
            {
                var start = DefaultStartTime(DateBox.SelectedDate ?? DateTime.Today);
                StartTimeBox.Text = start.ToString("HH:mm");
                EndTimeBox.Text = start.Add(TimeSpan.FromHours(1)).ToString("HH:mm");
            }
        }

        ApplyAllDayState();
    }

    private void ApplyAllDayState()
    {
        var allDay = AllDayCheckBox.IsChecked == true;
        StartTimeBox.IsEnabled = !allDay;
        EndTimeBox.IsEnabled = !allDay;

        if (allDay)
        {
            StartTimeBox.Text = "00:00";
            EndTimeBox.Text = string.Empty;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ValidateAndBuild();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    /// <summary>回车在输入框内不触发默认按钮（描述框需要回车换行）。</summary>
    private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ValidateAndBuild();
        }
    }

    private void ValidateAndBuild()
    {
        ValidationText.Visibility = Visibility.Collapsed;

        var title = TitleBox.Text.Trim();
        if (title.Length == 0)
        {
            ShowValidation("请输入日程标题。", TitleBox);
            return;
        }

        if (DateBox.SelectedDate is null)
        {
            ShowValidation("请选择日程日期。", DateBox);
            return;
        }

        var date = DateBox.SelectedDate.Value.Date;
        var allDay = AllDayCheckBox.IsChecked == true;
        var startLocal = date;
        DateTime? endLocal = null;

        if (!allDay)
        {
            if (!TryParseTime(StartTimeBox.Text, out var startTime))
            {
                ShowValidation("开始时间格式不正确，请使用 HH:mm，例如 09:30。", StartTimeBox);
                return;
            }

            startLocal = date + startTime;

            if (!string.IsNullOrWhiteSpace(EndTimeBox.Text))
            {
                if (!TryParseTime(EndTimeBox.Text, out var endTime))
                {
                    ShowValidation("结束时间格式不正确，请使用 HH:mm，例如 10:30。", EndTimeBox);
                    return;
                }

                if (endTime < startTime)
                {
                    ShowValidation("结束时间不能早于开始时间。", EndTimeBox);
                    return;
                }

                endLocal = date + endTime;
            }
        }

        var reminder = 0;
        var reminderText = ReminderBox.Text.Trim();
        if (reminderText.Length > 0 &&
            (!int.TryParse(reminderText, out reminder) || reminder < 0 || reminder > MaxReminderMinutes))
        {
            ShowValidation("提前提醒分钟数需为 0-" + MaxReminderMinutes + " 之间的整数。", ReminderBox);
            return;
        }

        var item = _original is null ? new ScheduleItem { Source = ScheduleSource.Manual } : Clone(_original);
        item.Title = title;
        item.StartLocal = startLocal;
        item.EndLocal = endLocal;
        item.AllDay = allDay;
        item.Location = NormalizeOptional(LocationBox.Text);
        item.Description = NormalizeOptional(DescriptionBox.Text);
        item.ReminderMinutesBefore = reminder;

        if (_original is null)
        {
            // 新增日程时记录当前选定的 iCloud 日历，便于同步服务确定目标。
            var caldav = AppServices.Config.Config.CalDav;
            item.CalendarHref = caldav.CalendarHref;
            item.CalendarName = caldav.CalendarDisplayName;
        }

        Result = item;
        DialogResult = true;
    }

    private static bool TryParseTime(string? text, out TimeSpan time)
    {
        time = default;
        var value = (text ?? string.Empty).Trim().Replace('：', ':');
        if (value.Length == 0)
        {
            return false;
        }

        var formats = new[] { "h\\:mm", "hh\\:mm" };
        if (!TimeSpan.TryParseExact(value, formats, CultureInfo.InvariantCulture, out time))
        {
            return false;
        }

        return time >= TimeSpan.Zero && time < TimeSpan.FromDays(1);
    }

    private static string? NormalizeOptional(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        return value.Length == 0 ? null : value;
    }

    private void ShowValidation(string message, Control? focusTarget = null)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
        focusTarget?.Focus();
    }

    /// <summary>编辑时复制一份，避免用户取消后内存中的原对象已被改动。</summary>
    private static ScheduleItem Clone(ScheduleItem source)
    {
        return new ScheduleItem
        {
            Id = source.Id,
            Uid = source.Uid,
            Title = source.Title,
            Location = source.Location,
            Description = source.Description,
            StartLocal = source.StartLocal,
            EndLocal = source.EndLocal,
            ReminderMinutesBefore = source.ReminderMinutesBefore,
            AllDay = source.AllDay,
            Source = source.Source,
            ScreenshotPath = source.ScreenshotPath,
            Supplement = source.Supplement,
            RawLlmJson = source.RawLlmJson,
            CalendarHref = source.CalendarHref,
            CalendarName = source.CalendarName,
            RemoteHref = source.RemoteHref,
            ETag = source.ETag,
            IsSynced = source.IsSynced,
            SyncError = source.SyncError,
            CreatedAt = source.CreatedAt,
            LastSyncedAt = source.LastSyncedAt,
            LastNotifiedAt = source.LastNotifiedAt,
            DeletedAt = source.DeletedAt
        };
    }
}
