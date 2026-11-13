using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutoAddSchedule.Core;
using AutoAddSchedule.Services;
using AutoAddSchedule.UI.Dialogs;

namespace AutoAddSchedule.UI.Views;

/// <summary>
/// 日程页：按开始时间升序列出全部日程，支持新增、编辑、删除与手动刷新同步。
/// 删除采用「先软删除、iCloud 删除成功后再硬删除」的策略，失败时保留待重试。
/// </summary>
public partial class ScheduleListView : UserControl
{
    private readonly Brush _successBrush;
    private readonly Brush _dangerBrush;
    private readonly Brush _secondaryBrush;

    private bool _subscribed;
    private bool _busy;

    public ScheduleListView()
    {
        InitializeComponent();

        _successBrush = ResolveBrush("SuccessBrush", Color.FromRgb(0x1F, 0x9D, 0x55));
        _dangerBrush = ResolveBrush("DangerBrush", Color.FromRgb(0xD9, 0x30, 0x26));
        _secondaryBrush = ResolveBrush("TextSecondaryBrush", Color.FromRgb(0x6B, 0x72, 0x80));
    }

    private static Brush ResolveBrush(string key, Color fallback)
    {
        return Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
    }

    /// <summary>重新读取本地日程并刷新列表，尽量保留原来的选中项。</summary>
    private void RefreshList()
    {
        var selectedId = (ScheduleList.SelectedItem as ScheduleRow)?.Id;
        var rows = AppServices.Schedules.GetAll().Select(ScheduleRow.FromItem).ToList();

        ScheduleList.ItemsSource = rows;
        CountText.Text = "共 " + rows.Count + " 条";
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (!string.IsNullOrEmpty(selectedId))
        {
            var row = rows.FirstOrDefault(item => item.Id == selectedId);
            if (row is not null)
            {
                ScheduleList.SelectedItem = row;
            }
        }

        UpdateSelectionButtons();
    }

    private void UpdateSelectionButtons()
    {
        var hasSelection = !_busy && ScheduleList.SelectedItem is ScheduleRow;
        EditButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        AddButton.IsEnabled = !busy;
        SyncButton.IsEnabled = !busy;
        UpdateSelectionButtons();

        if (!string.IsNullOrWhiteSpace(status))
        {
            SetStatus(status, _secondaryBrush);
        }
    }

    private void SetStatus(string text, Brush brush)
    {
        StatusText.Text = text;
        StatusText.Foreground = brush;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var item = ScheduleUiActions.Add(Window.GetWindow(this), DateTime.Today);
        if (item is not null)
        {
            SetStatus("已新增日程「" + item.Title + "」。", _successBrush);
            _ = SyncAfterSaveAsync(item);
        }
    }

    private void EditButton_Click(object sender, RoutedEventArgs e) => EditSelected();

    private void ScheduleList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => EditSelected();

    private void EditSelected()
    {
        if (ScheduleList.SelectedItem is not ScheduleRow row)
        {
            return;
        }

        var original = AppServices.Schedules.Find(row.Id);
        if (original is null)
        {
            return;
        }

        var updated = ScheduleUiActions.Edit(Window.GetWindow(this), original);
        if (updated is not null)
        {
            SetStatus("已保存日程「" + updated.Title + "」。", _successBrush);
            RefreshList();
            _ = SyncAfterSaveAsync(updated);
        }
    }

    private async Task SyncAfterSaveAsync(ScheduleItem item)
    {
        var message = await ScheduleUiActions.PushAsync(item);
        if (!string.IsNullOrWhiteSpace(message))
        {
            var synced = item.IsSynced;
            SetStatus("「" + item.Title + "」" + message, synced ? _successBrush : _dangerBrush);
        }

        RefreshList();
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (ScheduleList.SelectedItem is not ScheduleRow row)
        {
            return;
        }

        var item = AppServices.Schedules.Find(row.Id);
        if (item is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            Window.GetWindow(this),
            "确定删除日程「" + item.Title + "」吗？" + Environment.NewLine + Environment.NewLine +
            "本地会先移除该日程；如果它已同步到 iCloud，也会尝试删除 iCloud 上的事件，" +
            "删除失败时可稍后点击「刷新同步」重试。",
            "删除日程",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true, "正在删除「" + item.Title + "」…");
        try
        {
            AppServices.Schedules.MarkDeleted(item.Id);

            SyncOutcome outcome;
            try
            {
                outcome = await AppServices.Sync.DeleteRemoteAsync(item);
            }
            catch (Exception ex)
            {
                AppLog.Error("删除 iCloud 事件失败", ex);
                outcome = SyncOutcome.Failure(ex.Message);
            }

            if (outcome.Synced)
            {
                AppServices.Schedules.RemoveHard(item.Id);
                SetStatus("已删除「" + item.Title + "」并同步删除 iCloud 事件。", _successBrush);
            }
            else
            {
                SetStatus(
                    "已从列表删除「" + item.Title + "」；iCloud 删除失败：" +
                    (outcome.Error ?? "未知错误") + "。稍后点击「刷新同步」可重试。",
                    _dangerBrush);
            }
        }
        finally
        {
            SetBusy(false);
            RefreshList();
        }
    }

    private async void SyncButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "正在同步未同步的日程…");
        SyncButton.Content = "同步中…";
        try
        {
            var successCount = await AppServices.Sync.PushPendingAsync();
            RefreshList();

            var all = AppServices.Schedules.GetAll();
            var failedCount = all.Count(item => !item.IsSynced);

            if (failedCount == 0)
            {
                SetStatus(
                    successCount > 0
                        ? "同步完成：成功 " + successCount + " 条，全部日程已同步到 iCloud。"
                        : "同步完成：所有日程均无需同步。",
                    _successBrush);
            }
            else
            {
                SetStatus(
                    "同步完成：成功 " + successCount + " 条，" + failedCount +
                    " 条仍未同步，请检查网络连接与「设置」中的 iCloud 日历配置。",
                    _dangerBrush);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("手动刷新同步失败", ex);
            SetStatus("同步失败：" + ex.Message + "。请检查网络连接与 iCloud 日历配置。", _dangerBrush);
        }
        finally
        {
            SyncButton.Content = "刷新同步";
            SetBusy(false);
        }
    }

    private void ScheduleList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectionButtons();

    private void ScheduleListView_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            AppServices.Schedules.Changed += OnSchedulesChanged;
            _subscribed = true;
        }

        RefreshList();
    }

    private void ScheduleListView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed)
        {
            AppServices.Schedules.Changed -= OnSchedulesChanged;
            _subscribed = false;
        }
    }

    private void OnSchedulesChanged(object? sender, EventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        Dispatcher.InvokeAsync(RefreshList);
    }
}

/// <summary>日程行的展示模型：把 ScheduleItem 转成界面直接绑定的文字与颜色。</summary>
internal sealed class ScheduleRow
{
    private static readonly Brush SyncedBrush = CreateBrush(0x1F, 0x9D, 0x55);
    private static readonly Brush FailedBrush = CreateBrush(0xD9, 0x30, 0x26);
    private static readonly Brush PendingBrush = CreateBrush(0xC7, 0x77, 0x00);

    public required ScheduleItem Item { get; init; }

    public string Id => Item.Id;

    /// <summary>列表「开始时间」列：2026-11-13 09:00</summary>
    public string TimeText { get; init; } = string.Empty;

    /// <summary>日历当天列表：09:00 - 10:00 或 全天</summary>
    public string DayTimeText { get; init; } = string.Empty;

    public string Title => Item.Title;

    public string LocationText { get; init; } = string.Empty;

    /// <summary>日历当天列表的第二行：地点 · 提醒。</summary>
    public string DetailText { get; init; } = string.Empty;

    public string ReminderText { get; init; } = string.Empty;

    public string StatusText { get; init; } = string.Empty;

    public Brush StatusBrush { get; init; } = PendingBrush;

    public static ScheduleRow FromItem(ScheduleItem item)
    {
        var location = item.Location?.Trim() ?? string.Empty;
        var reminder = item.ReminderMinutesBefore > 0
            ? "提前 " + item.ReminderMinutesBefore + " 分钟"
            : "不提醒";

        var detailParts = new List<string>();
        if (location.Length > 0)
        {
            detailParts.Add(location);
        }

        if (item.ReminderMinutesBefore > 0)
        {
            detailParts.Add(reminder);
        }

        var dayTime = item.AllDay
            ? "全天"
            : item.EndLocal.HasValue && item.EndLocal.Value > item.StartLocal
                ? item.StartLocal.ToString("HH:mm") + " - " + item.EndLocal.Value.ToString("HH:mm")
                : item.StartLocal.ToString("HH:mm");

        string statusText;
        Brush statusBrush;
        if (item.IsSynced && string.IsNullOrWhiteSpace(item.SyncError))
        {
            statusText = "已同步";
            statusBrush = SyncedBrush;
        }
        else if (!string.IsNullOrWhiteSpace(item.SyncError))
        {
            statusText = "同步失败";
            statusBrush = FailedBrush;
        }
        else
        {
            statusText = "未同步";
            statusBrush = PendingBrush;
        }

        return new ScheduleRow
        {
            Item = item,
            TimeText = TimeFormat.FormatDateTime(item.StartLocal),
            DayTimeText = dayTime,
            LocationText = location,
            DetailText = string.Join(" · ", detailParts),
            ReminderText = reminder,
            StatusText = statusText,
            StatusBrush = statusBrush
        };
    }

    private static Brush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// 日历页与日程页共用的日程操作：新增、编辑、保存后同步 iCloud。
/// （本模块只允许改动各视图自身的代码文件，故共用的辅助类集中放在这里。）
/// </summary>
internal static class ScheduleUiActions
{
    /// <summary>打开新增对话框并写入本地；取消返回 null。</summary>
    public static ScheduleItem? Add(Window? owner, DateTime defaultDate)
    {
        var dialog = new ScheduleEditDialog(null, defaultDate) { Owner = owner };
        if (dialog.ShowDialog() != true || dialog.Result is null)
        {
            return null;
        }

        AppServices.Schedules.Add(dialog.Result);
        return dialog.Result;
    }

    /// <summary>打开编辑对话框并保存修改；取消返回 null。</summary>
    public static ScheduleItem? Edit(Window? owner, ScheduleItem original)
    {
        var dialog = new ScheduleEditDialog(original) { Owner = owner };
        if (dialog.ShowDialog() != true || dialog.Result is null)
        {
            return null;
        }

        var updated = dialog.Result;
        // 修改开始时间或提醒提前量后重置提醒标记，让到点提醒可以重新触发。
        if (original.StartLocal != updated.StartLocal ||
            original.ReminderMinutesBefore != updated.ReminderMinutesBefore)
        {
            updated.LastNotifiedAt = null;
        }

        updated.IsSynced = false;
        updated.SyncError = null;
        AppServices.Schedules.Update(updated);
        return updated;
    }

    /// <summary>
    /// 手动保存后尝试同步到 iCloud；未配置 iCloud 日历时不提示。
    /// 返回需要展示给用户的状态文本，无需提示时返回 null。
    /// </summary>
    public static async Task<string?> PushAsync(ScheduleItem item)
    {
        var caldav = AppServices.Config.Config.CalDav;
        if (!caldav.IsConfigured || string.IsNullOrWhiteSpace(caldav.CalendarHref))
        {
            return null;
        }

        try
        {
            var outcome = await AppServices.Sync.PushAsync(item);
            if (outcome.Synced)
            {
                return "已同步到 iCloud 日历。";
            }

            MarkFailed(item, outcome.Error);
            return "已保存到本地，同步 iCloud 失败：" + (outcome.Error ?? "未知错误") +
                   "。稍后可在「刷新同步」中重试。";
        }
        catch (Exception ex)
        {
            AppLog.Error("保存日程后同步 iCloud 失败", ex);
            MarkFailed(item, ex.Message);
            return "已保存到本地，同步 iCloud 失败：" + ex.Message + "。稍后可在「刷新同步」中重试。";
        }
    }

    private static void MarkFailed(ScheduleItem item, string? error)
    {
        item.IsSynced = false;
        item.SyncError = string.IsNullOrWhiteSpace(error) ? "未知错误" : error;
        AppServices.Schedules.Update(item);
    }
}
