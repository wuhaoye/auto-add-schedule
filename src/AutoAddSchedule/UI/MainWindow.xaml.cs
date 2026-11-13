using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoAddSchedule.Core;
using AutoAddSchedule.Services;
using AutoAddSchedule.UI.Views;

namespace AutoAddSchedule.UI;

/// <summary>
/// 主窗口外壳：左侧导航（日历 / 日程 / 设置）+ 右侧内容区。
/// 关闭窗口只隐藏到托盘，程序继续常驻；真正退出由托盘菜单或设置页触发。
/// </summary>
public partial class MainWindow : Window
{
    private readonly CalendarView _calendarView = new();
    private readonly ScheduleListView _scheduleListView = new();
    private readonly SettingsView _settingsView = new();

    private string _currentPage = "calendar";

    public MainWindow()
    {
        InitializeComponent();
        ShowPage("calendar");
        UpdateHotkeyHint();
        AppServices.Config.Changed += OnConfigChanged;
    }

    /// <summary>切换到指定页面：calendar / list / settings。供 App 从托盘菜单或启动参数调用。</summary>
    public void NavigateTo(string key) => ShowPage(key);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        FitToWorkArea();
    }

    /// <summary>
    /// 按当前屏幕工作区收缩窗口并手动居中。
    /// 注意：只在 OnSourceInitialized 里改尺寸时，WPF 仍会按原始尺寸计算 CenterScreen 位置，
    /// 导致窗口顶部跑到屏幕外、标题栏不可见且无法拖动。因此这里改为手动定位并夹在工作区内。
    /// </summary>
    private void FitToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            return;
        }

        var width = Math.Min(Width, Math.Max(MinWidth, workArea.Width - 24));
        var height = Math.Min(Height, Math.Max(MinHeight, workArea.Height - 24));

        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = width;
        Height = height;
        Left = workArea.Left + Math.Max(0, (workArea.Width - width) / 2);
        Top = workArea.Top + Math.Max(0, (workArea.Height - height) / 2);
    }

    /// <summary>刷新左侧底部的快捷键提示（保存设置后调用）。</summary>
    public void UpdateHotkeyHint()
    {
        var gesture = HotkeyGesture.Parse(AppServices.Config.Config.Hotkey.Gesture) ?? HotkeyGesture.Default;
        HotkeyHintText.Text = "快捷键 " + gesture.DisplayText + " 截图添加日程";
    }

    private void OnConfigChanged(object? sender, EventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        Dispatcher.InvokeAsync(UpdateHotkeyHint);
    }

    private void ShowPage(string key)
    {
        var page = key switch
        {
            "settings" => "settings",
            "list" => "list",
            _ => "calendar"
        };

        UserControl view = page switch
        {
            "settings" => _settingsView,
            "list" => _scheduleListView,
            _ => _calendarView
        };

        if (!ReferenceEquals(ContentHost.Content, view))
        {
            ContentHost.Content = view;
        }

        _currentPage = page;
        UpdateNavState();
    }

    /// <summary>高亮当前导航项（Tag="selected" 由导航按钮模板识别）。</summary>
    private void UpdateNavState()
    {
        NavCalendarButton.Tag = _currentPage == "calendar" ? "selected" : null;
        NavListButton.Tag = _currentPage == "list" ? "selected" : null;
        NavSettingsButton.Tag = _currentPage == "settings" ? "selected" : null;
    }

    private void NavCalendar_Click(object sender, RoutedEventArgs e) => ShowPage("calendar");

    private void NavList_Click(object sender, RoutedEventArgs e) => ShowPage("list");

    private void NavSettings_Click(object sender, RoutedEventArgs e) => ShowPage("settings");

    protected override void OnClosing(CancelEventArgs e)
    {
        // 关闭窗口 = 隐藏到托盘，不退出程序。
        if (!App.IsExiting)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }
}