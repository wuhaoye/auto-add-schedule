using System.Drawing;
using System.Windows.Forms;
using AutoAddSchedule.Core;

namespace AutoAddSchedule.Services;

/// <summary>使用 WinForms NotifyIcon 实现托盘图标、菜单与气泡通知。</summary>
public sealed class TrayService : ITrayService
{
    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _menu;
    private Icon? _icon;
    private bool _disposed;

    public event EventHandler? OpenRequested;

    public event EventHandler? CaptureRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? ExitRequested;

    public void Show()
    {
        if (_notifyIcon is not null)
        {
            return;
        }

        _icon = LoadAppIcon();
        _menu = new ContextMenuStrip();
        _menu.Items.Add("打开主界面", null, (_, _) => Raise(OpenRequested));
        _menu.Items.Add("截图添加日程", null, (_, _) => Raise(CaptureRequested));
        _menu.Items.Add("设置", null, (_, _) => Raise(SettingsRequested));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => Raise(ExitRequested));

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "AutoAddSchedule：自动添加日程",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _notifyIcon.DoubleClick += (_, _) => Raise(OpenRequested);
    }

    public void ShowBalloon(string title, string message, BalloonIcon icon = BalloonIcon.Info)
    {
        if (_notifyIcon is null)
        {
            return;
        }

        try
        {
            var toolTipIcon = icon switch
            {
                BalloonIcon.Warning => ToolTipIcon.Warning,
                BalloonIcon.Error => ToolTipIcon.Error,
                _ => ToolTipIcon.Info
            };

            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = message;
            _notifyIcon.BalloonTipIcon = toolTipIcon;
            _notifyIcon.ShowBalloonTip(6000);
        }
        catch (Exception ex)
        {
            AppLog.Warn("显示托盘通知失败：" + ex.Message);
        }
    }

    public void UpdateTooltip(string text)
    {
        if (_notifyIcon is null)
        {
            return;
        }

        // NotifyIcon.Text 最长 127 个字符。
        _notifyIcon.Text = text.Length > 120 ? text[..120] : text;
    }

    private static void Raise(EventHandler? handler)
    {
        try
        {
            handler?.Invoke(null, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppLog.Error("托盘菜单处理异常", ex);
        }
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            var iconPath = System.IO.Path.Combine(AppPaths.InstallDirectory, "Assets", "app.ico");
            if (System.IO.File.Exists(iconPath))
            {
                return new Icon(iconPath);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("加载应用图标失败：" + ex.Message);
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _menu?.Dispose();
        _menu = null;
        _icon?.Dispose();
        _icon = null;
    }
}
