using System.Windows;
using System.Windows.Threading;
using AutoAddSchedule.Core;
using AutoAddSchedule.Services;
using AutoAddSchedule.UI;

namespace AutoAddSchedule;

public partial class App : Application
{
    private static Mutex? s_singleInstanceMutex;
    private bool _ownsSingleInstance;

    /// <summary>是否正在退出（区分「关闭窗口=隐藏」与「真正退出」）。</summary>
    public static bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _ownsSingleInstance = TryAcquireSingleInstance();
        if (!_ownsSingleInstance)
        {
            MessageBox.Show("AutoAddSchedule 已经在运行，请在系统托盘查看。", "AutoAddSchedule",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        AppPaths.EnsureDirectories();
        AppLog.Init();
        AppLog.Info("应用启动。");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error("未处理异常", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("未观察的任务异常", args.Exception);
            args.SetObserved();
        };

        try
        {
            AppServices.Initialize();
        }
        catch (Exception ex)
        {
            AppLog.Error("初始化失败", ex);
            MessageBox.Show("程序初始化失败：" + ex.Message, "AutoAddSchedule",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // 托盘
        AppServices.Tray.OpenRequested += (_, _) => ShowMainWindow();
        AppServices.Tray.CaptureRequested += (_, _) => BeginCapture();
        AppServices.Tray.SettingsRequested += (_, _) => ShowMainWindow("settings");
        AppServices.Tray.ExitRequested += (_, _) => ExitApplication();
        AppServices.Tray.Show();

        // 全局快捷键
        AppServices.Hotkey.Pressed += (_, _) => BeginCapture();
        if (!RegisterHotkey(AppServices.Config.Config.Hotkey.Gesture) &&
            !string.IsNullOrWhiteSpace(AppServices.Hotkey.LastError))
        {
            AppServices.Tray.ShowBalloon("快捷键注册失败", AppServices.Hotkey.LastError, BalloonIcon.Warning);
        }

        // 日程提醒
        AppServices.Reminders.Start();

        // 主窗口
        var window = new MainWindow();
        MainWindow = window;

        var config = AppServices.Config.Config;
        var needSetup = !config.FirstRunCompleted || !config.Llm.IsConfigured || !config.CalDav.IsConfigured;
        if (needSetup)
        {
            ShowMainWindow("settings");
        }
        else if (!config.General.StartMinimizedToTray)
        {
            ShowMainWindow();
        }

        // 调试/QA 参数（开发期使用）：
        //   --page calendar|list|settings  启动后直接显示指定页面
        //   --capture                      启动后自动进入截图流程（用于验证截图界面）
        var qaPage = ReadArg(e.Args, "--page");
        if (!string.IsNullOrWhiteSpace(qaPage))
        {
            ShowMainWindow(qaPage);
        }

        if (e.Args.Any(arg => string.Equals(arg, "--capture", StringComparison.OrdinalIgnoreCase)))
        {
            Dispatcher.BeginInvoke(async () =>
            {
                await Task.Delay(1200);
                await AppServices.Capture.RunAsync();
            });
        }

        var gesture = HotkeyGesture.Parse(config.Hotkey.Gesture) ?? HotkeyGesture.Default;
        AppServices.Tray.UpdateTooltip("AutoAddSchedule：按 " + gesture.DisplayText + " 截图添加日程");
        AppServices.Tray.ShowBalloon("AutoAddSchedule 已启动", "按 " + gesture.DisplayText + " 截图添加日程。");
    }

    private static string? ReadArg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    public static void ShowMainWindow(string? navigateTo = null)
    {
        if (Current?.MainWindow is not MainWindow window)
        {
            return;
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        if (!string.IsNullOrWhiteSpace(navigateTo))
        {
            window.NavigateTo(navigateTo);
        }
    }

    public static void BeginCapture()
    {
        if (Current is not App app)
        {
            return;
        }

        app.Dispatcher.BeginInvoke(async () => await AppServices.Capture.RunAsync());
    }

    public static void ExitApplication()
    {
        IsExiting = true;
        if (Current is App app)
        {
            app.Shutdown();
        }
    }

    /// <summary>注册全局快捷键；失败弹出提示。修改设置后也调用这里。</summary>
    public static bool RegisterHotkey(string gestureText)
    {
        var gesture = HotkeyGesture.Parse(gestureText) ?? HotkeyGesture.Default;
        var success = AppServices.Hotkey.Register(gesture);
        if (!success)
        {
            AppServices.Notifications.Show("快捷键注册失败",
                AppServices.Hotkey.LastError ?? "未知原因，请在设置中更换快捷键。",
                NotificationKind.Warning);
        }

        return success;
    }

    /// <summary>设置页保存后调用：持久化配置并应用快捷键、开机自启等。</summary>
    public static void ApplySettings()
    {
        try
        {
            AppServices.Config.Config.FirstRunCompleted = true;
            AppServices.Config.Save();
            RegisterHotkey(AppServices.Config.Config.Hotkey.Gesture);
            AppServices.AutoStart.SetEnabled(AppServices.Config.Config.General.AutoStart);

            var gesture = HotkeyGesture.Parse(AppServices.Config.Config.Hotkey.Gesture) ?? HotkeyGesture.Default;
            AppServices.Tray.UpdateTooltip("AutoAddSchedule：按 " + gesture.DisplayText + " 截图添加日程");
            AppServices.Notifications.Show("设置已保存", "新的设置已生效。");
        }
        catch (Exception ex)
        {
            AppLog.Error("应用设置失败", ex);
            MessageBox.Show("保存设置失败：" + ex.Message, "AutoAddSchedule",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool TryAcquireSingleInstance()
    {
        try
        {
            s_singleInstanceMutex = new Mutex(initiallyOwned: true, @"Global\AutoAddScheduleSingleInstance", out var createdNew);
            return createdNew;
        }
        catch
        {
            return true;
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("界面线程未处理异常", e.Exception);
        e.Handled = true;
        MessageBox.Show("发生未处理的错误：" + e.Exception.Message, "AutoAddSchedule",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppServices.Shutdown();
        try
        {
            if (_ownsSingleInstance)
            {
                s_singleInstanceMutex?.ReleaseMutex();
            }
        }
        catch
        {
            // 忽略释放失败。
        }

        s_singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}