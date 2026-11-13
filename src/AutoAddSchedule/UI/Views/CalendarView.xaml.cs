using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutoAddSchedule.Core;
using AutoAddSchedule.Services;
using AutoAddSchedule.UI.Dialogs;

namespace AutoAddSchedule.UI.Views;

/// <summary>
/// 日历页：自绘月视图（周一为第一天，6 行 × 7 列），选中日期下方显示当天日程。
/// 日期格中最多显示 3 条日程，超出显示「+N」。
/// </summary>
public partial class CalendarView : UserControl
{
    private const int CellCount = 42;
    private const int MaxEntriesPerCell = 3;

    private static readonly string[] WeekdayNames =
    {
        "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六"
    };

    private readonly List<DayCell> _cells = new();

    private readonly Brush _primaryBrush;
    private readonly Brush _selectionBackgroundBrush;
    private readonly Brush _entryBrush;
    private readonly Brush _mutedBrush;
    private readonly Brush _secondaryBrush;
    private readonly Brush _cellBorderBrush;

    private DateTime _displayMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _selectedDate = DateTime.Today;
    private bool _subscribed;

    public CalendarView()
    {
        InitializeComponent();

        _primaryBrush = ResolveBrush("PrimaryBrush", Color.FromRgb(0x2F, 0x6F, 0xED));
        _selectionBackgroundBrush = ResolveBrush("HoverBrush", Color.FromRgb(0xEE, 0xF2, 0xFA));
        _entryBrush = ResolveBrush("TextSecondaryBrush", Color.FromRgb(0x6B, 0x72, 0x80));
        _mutedBrush = new SolidColorBrush(Color.FromRgb(0xB9, 0xC0, 0xCA));
        _secondaryBrush = ResolveBrush("TextSecondaryBrush", Color.FromRgb(0x6B, 0x72, 0x80));
        _cellBorderBrush = new SolidColorBrush(Color.FromRgb(0xEC, 0xEF, 0xF3));

        BuildCells();
        Refresh();
    }

    /// <summary>选中指定日期（用于外部跳转，例如「今天」）。</summary>
    public void SelectDate(DateTime date)
    {
        _selectedDate = date.Date;
        _displayMonth = new DateTime(date.Year, date.Month, 1);
        Refresh();
    }

    private static Brush ResolveBrush(string key, Color fallback)
    {
        return Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
    }

    /// <summary>创建 42 个日期格（只创建一次，之后只更新内容）。</summary>
    private void BuildCells()
    {
        CalendarHost.Children.Clear();
        _cells.Clear();

        for (var i = 0; i < CellCount; i++)
        {
            var dayNumber = new TextBlock
            {
                FontSize = 10.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var numberHost = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Background = Brushes.Transparent,
                Child = dayNumber
            };

            var entries = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };

            var moreText = new TextBlock
            {
                FontSize = 9.5,
                Margin = new Thickness(0, 1, 0, 0),
                Visibility = Visibility.Collapsed
            };

            // 「+N」浮在单元格右下角，不再单独占一行，保证单元格较矮时也能显示 3 条日程。
            moreText.HorizontalAlignment = HorizontalAlignment.Right;
            moreText.VerticalAlignment = VerticalAlignment.Bottom;
            moreText.Margin = new Thickness(0, 0, 2, 0);

            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(numberHost, 0);
            Grid.SetRow(entries, 1);
            Grid.SetRowSpan(moreText, 2);
            content.Children.Add(numberHost);
            content.Children.Add(entries);
            content.Children.Add(moreText);

            var root = new Border
            {
                Margin = new Thickness(0.5),
                Padding = new Thickness(3, 2, 3, 2),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = _cellBorderBrush,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ClipToBounds = true,
                Child = content
            };

            var cell = new DayCell
            {
                Root = root,
                DayNumber = dayNumber,
                NumberHost = numberHost,
                Entries = entries,
                MoreText = moreText
            };

            root.MouseLeftButtonUp += (_, _) => SelectDate(cell.Date);
            _cells.Add(cell);
            CalendarHost.Children.Add(root);
        }
    }

    private void Refresh()
    {
        var byDay = AppServices.Schedules.GetAll()
            .GroupBy(item => item.StartLocal.Date)
            .ToDictionary(group => group.Key, group => group.ToList());

        RefreshMonth(byDay);
        RefreshSelectedDay(byDay);
    }

    /// <summary>根据当前展示月份刷新 42 个日期格。</summary>
    private void RefreshMonth(Dictionary<DateTime, List<ScheduleItem>> byDay)
    {
        MonthTitleText.Text = _displayMonth.ToString("yyyy年M月");

        var first = new DateTime(_displayMonth.Year, _displayMonth.Month, 1);
        // 周一作为一周第一天：DayOfWeek 中周日 = 0，需要转换成「周一 = 0」。
        var offset = ((int)first.DayOfWeek + 6) % 7;
        var start = first.AddDays(-offset);

        for (var i = 0; i < _cells.Count; i++)
        {
            var date = start.AddDays(i);
            var cell = _cells[i];
            cell.Date = date;

            var inMonth = date.Month == _displayMonth.Month && date.Year == _displayMonth.Year;
            var isToday = date.Date == DateTime.Today;
            var isSelected = date.Date == _selectedDate.Date;

            cell.DayNumber.Text = date.Day.ToString();
            cell.DayNumber.FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal;
            cell.NumberHost.Background = isToday ? _primaryBrush : Brushes.Transparent;

            if (isToday)
            {
                cell.DayNumber.Foreground = Brushes.White;
            }
            else
            {
                cell.DayNumber.Foreground = inMonth
                    ? ResolveBrush("TextPrimaryBrush", Color.FromRgb(0x1F, 0x23, 0x29))
                    : _mutedBrush;
            }

            if (isSelected)
            {
                cell.Root.Background = _selectionBackgroundBrush;
                cell.Root.BorderBrush = _primaryBrush;
            }
            else
            {
                cell.Root.Background = isToday ? _selectionBackgroundBrush : Brushes.Transparent;
                cell.Root.BorderBrush = _cellBorderBrush;
            }

            UpdateCellEntries(cell, byDay.TryGetValue(date.Date, out var items) ? items : null, inMonth);
        }
    }

    /// <summary>更新单个日期格内的日程摘要（最多 3 条 + 「+N」）。</summary>
    private void UpdateCellEntries(DayCell cell, List<ScheduleItem>? items, bool inMonth)
    {
        cell.Entries.Children.Clear();
        var count = items?.Count ?? 0;
        cell.Entries.Margin = new Thickness(0, 2, count > MaxEntriesPerCell ? 14 : 0, 0);

        if (items is not null)
        {
            foreach (var item in items.Take(MaxEntriesPerCell))
            {
                var timeText = item.AllDay ? "全天" : item.StartLocal.ToString("HH:mm");
                cell.Entries.Children.Add(new TextBlock
                {
                    Text = timeText + " " + item.Title,
                    FontSize = 9.5,
                    Margin = new Thickness(0, 1, 0, 0),
                    Foreground = inMonth ? _entryBrush : _mutedBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap
                });
            }
        }

        if (count > MaxEntriesPerCell)
        {
            cell.MoreText.Text = "+" + (count - MaxEntriesPerCell);
            cell.MoreText.Foreground = _secondaryBrush;
            cell.MoreText.Visibility = Visibility.Visible;
        }
        else
        {
            cell.MoreText.Text = string.Empty;
            cell.MoreText.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshSelectedDay(Dictionary<DateTime, List<ScheduleItem>> byDay)
    {
        var items = byDay.TryGetValue(_selectedDate.Date, out var list) ? list : new List<ScheduleItem>();
        var rows = items.Select(ScheduleRow.FromItem).ToList();

        DayScheduleList.ItemsSource = rows;
        DayEmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        SelectedDayTitleText.Text = _selectedDate.ToString("M月d日") + " " + WeekdayNames[(int)_selectedDate.DayOfWeek];
        SelectedDayCountText.Text = rows.Count == 0 ? string.Empty : "共 " + rows.Count + " 条日程";
    }

    private void NavigateMonth(int delta)
    {
        _displayMonth = _displayMonth.AddMonths(delta);
        var first = new DateTime(_displayMonth.Year, _displayMonth.Month, 1);
        var today = DateTime.Today;
        _selectedDate = today.Year == first.Year && today.Month == first.Month ? today : first;
        Refresh();
    }

    private void PrevMonth_Click(object sender, RoutedEventArgs e) => NavigateMonth(-1);

    private void NextMonth_Click(object sender, RoutedEventArgs e) => NavigateMonth(1);

    private void Today_Click(object sender, RoutedEventArgs e) => SelectDate(DateTime.Today);

    private void AddSchedule_Click(object sender, RoutedEventArgs e)
    {
        var item = ScheduleUiActions.Add(Window.GetWindow(this), _selectedDate);
        if (item is not null)
        {
            _ = SyncAfterSaveAsync(item);
        }
    }

    private void DayScheduleList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DayScheduleList.SelectedItem is not ScheduleRow row)
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
            _ = SyncAfterSaveAsync(updated);
        }
    }

    private async Task SyncAfterSaveAsync(ScheduleItem item)
    {
        var message = await ScheduleUiActions.PushAsync(item);
        if (!string.IsNullOrWhiteSpace(message))
        {
            AppServices.Notifications.Show("日程已保存", message, NotificationKind.Info);
        }
    }

    private void CalendarView_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            AppServices.Schedules.Changed += OnSchedulesChanged;
            _subscribed = true;
        }

        Refresh();
    }

    private void CalendarView_Unloaded(object sender, RoutedEventArgs e)
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

        Dispatcher.InvokeAsync(Refresh);
    }

    /// <summary>一个日期格及其内部控件引用。</summary>
    private sealed class DayCell
    {
        public required Border Root { get; init; }

        public required TextBlock DayNumber { get; init; }

        public required Border NumberHost { get; init; }

        public required StackPanel Entries { get; init; }

        public required TextBlock MoreText { get; init; }

        public DateTime Date { get; set; }
    }
}