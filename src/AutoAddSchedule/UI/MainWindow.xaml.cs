using System.ComponentModel;
using System.Windows;
using AutoAddSchedule.UI.Views;

namespace AutoAddSchedule.UI;

public partial class MainWindow : Window
{
    private readonly CalendarView _calendarView = new();
    private readonly ScheduleListView _scheduleListView = new();
    private readonly SettingsView _settingsView = new();

    public MainWindow()
    {
        InitializeComponent();
        ShowPage("calendar");
    }

    /// <summary>切换到指定页面：calendar / list / settings。</summary>
    public void NavigateTo(string key) => ShowPage(key);

    private void ShowPage(string key)
    {
        switch (key)
        {
            case "settings":
                ContentHost.Content = _settingsView;
                break;
            case "list":
                ContentHost.Content = _scheduleListView;
                break;
            default:
                ContentHost.Content = _calendarView;
                break;
        }
    }

    private void NavCalendar_Click(object sender, RoutedEventArgs e) => ShowPage("calendar");

    private void NavList_Click(object sender, RoutedEventArgs e) => ShowPage("list");

    private void NavSettings_Click(object sender, RoutedEventArgs e) => ShowPage("settings");

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.IsExiting)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }
}
