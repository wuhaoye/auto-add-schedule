using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutoAddSchedule.Core;
using AutoAddSchedule.Services;

namespace AutoAddSchedule.UI.Views;

/// <summary>
/// 设置页：大模型 / iCloud 日历 / 快捷键 / 通用四个分组。
/// 密钥输入框为 PasswordBox，已保存时留空表示不修改。
/// </summary>
public partial class SettingsView : UserControl
{
    private readonly Brush _successBrush;
    private readonly Brush _dangerBrush;
    private readonly Brush _secondaryBrush;

    private HotkeyGesture _hotkeyGesture = HotkeyGesture.Default;

    public SettingsView()
    {
        InitializeComponent();

        _successBrush = ResolveBrush("SuccessBrush", Color.FromRgb(0x1F, 0x9D, 0x55));
        _dangerBrush = ResolveBrush("DangerBrush", Color.FromRgb(0xD9, 0x30, 0x26));
        _secondaryBrush = ResolveBrush("TextSecondaryBrush", Color.FromRgb(0x6B, 0x72, 0x80));

        LoadConfigToUi();
        UpdateKeyPlaceholders();
        SetStatus(HotkeyStatusText, "当前快捷键：" + _hotkeyGesture.DisplayText + "。点击输入框后可重新设置。", _secondaryBrush);
    }

    private static Brush ResolveBrush(string key, Color fallback)
    {
        return Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
    }

    private static void SetStatus(TextBlock target, string text, Brush brush)
    {
        target.Text = text;
        target.Foreground = brush;
    }

    // ===== 配置读取 =====

    private void LoadConfigToUi()
    {
        var config = AppServices.Config.Config;

        LlmBaseUrlBox.Text = config.Llm.BaseUrl;
        LlmModelBox.Text = config.Llm.Model;

        AppleIdBox.Text = config.CalDav.AppleId;
        AutoStartCheckBox.IsChecked = config.General.AutoStart;
        StartMinimizedCheckBox.IsChecked = config.General.StartMinimizedToTray;
        RetentionBox.Text = config.General.ScreenshotRetention.ToString();

        _hotkeyGesture = HotkeyGesture.Parse(config.Hotkey.Gesture) ?? HotkeyGesture.Default;
        HotkeyBox.Text = _hotkeyGesture.DisplayText;

        // 已保存的日历目标先放进下拉框，未重新读取日历列表时也能看到当前选择。
        CalendarComboBox.Items.Clear();
        if (!string.IsNullOrWhiteSpace(config.CalDav.CalendarHref) ||
            !string.IsNullOrWhiteSpace(config.CalDav.CalendarDisplayName))
        {
            var href = config.CalDav.CalendarHref ?? string.Empty;
            var info = new CalendarInfo
            {
                Href = href,
                Url = href,
                DisplayName = string.IsNullOrWhiteSpace(config.CalDav.CalendarDisplayName)
                    ? "（已保存的日历）"
                    : config.CalDav.CalendarDisplayName
            };
            CalendarComboBox.Items.Add(info);
            CalendarComboBox.SelectedIndex = 0;
        }
    }

    /// <summary>刷新两个密码框的「已保存」占位提示。</summary>
    private void UpdateKeyPlaceholders()
    {
        var config = AppServices.Config.Config;

        LlmKeyPlaceholder.Text = string.IsNullOrWhiteSpace(config.Llm.ApiKey)
            ? "尚未设置"
            : "已保存（留空表示不修改）";
        LlmKeyPlaceholder.Visibility = LlmKeyBox.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        CalDavPasswordPlaceholder.Text = string.IsNullOrWhiteSpace(config.CalDav.AppPassword)
            ? "尚未设置"
            : "已保存（留空表示不修改）";
        CalDavPasswordPlaceholder.Visibility =
            CalDavPasswordBox.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e) => UpdateKeyPlaceholders();

    // ===== 大模型 =====

    private bool TryApplyLlmUiToConfig(out string error)
    {
        error = string.Empty;
        var baseUrl = LlmBaseUrlBox.Text.Trim();
        var model = LlmModelBox.Text.Trim();

        if (baseUrl.Length == 0)
        {
            error = "请填写大模型 Base URL，例如 https://api.deepseek.com。";
            return false;
        }

        if (model.Length == 0)
        {
            error = "请填写模型名，例如 deepseek-flash。";
            return false;
        }

        var config = AppServices.Config.Config.Llm;
        config.BaseUrl = baseUrl;
        config.Model = model;
        var apiKey = LlmKeyBox.Password;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            config.ApiKey = apiKey;
        }

        return true;
    }

    private async void LlmTest_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = SnapshotLlm();
        if (!TryApplyLlmUiToConfig(out var error))
        {
            SetStatus(LlmStatusText, error, _dangerBrush);
            RestoreLlm(snapshot);
            return;
        }

        LlmTestButton.IsEnabled = false;
        SetStatus(LlmStatusText, "正在测试大模型连接…", _secondaryBrush);
        try
        {
            var result = await AppServices.Llm.TestConnectionAsync();
            var message = string.IsNullOrWhiteSpace(result) ? "连接成功，模型可用。" : "连接成功：" + result;
            SetStatus(LlmStatusText, message, _successBrush);
        }
        catch (Exception ex)
        {
            AppLog.Error("测试大模型连接失败", ex);
            SetStatus(LlmStatusText, "连接失败：" + ex.Message, _dangerBrush);
        }
        finally
        {
            RestoreLlm(snapshot);
            LlmTestButton.IsEnabled = true;
        }
    }

    private (string BaseUrl, string Model, string ApiKey) SnapshotLlm()
    {
        var config = AppServices.Config.Config.Llm;
        return (config.BaseUrl, config.Model, config.ApiKey);
    }

    private static void RestoreLlm((string BaseUrl, string Model, string ApiKey) snapshot)
    {
        var config = AppServices.Config.Config.Llm;
        config.BaseUrl = snapshot.BaseUrl;
        config.Model = snapshot.Model;
        config.ApiKey = snapshot.ApiKey;
    }

    // ===== iCloud 日历 =====

    private bool TryApplyCalDavUiToConfig(out string error)
    {
        error = string.Empty;
        var appleId = AppleIdBox.Text.Trim();
        if (appleId.Length == 0)
        {
            error = "请先填写 Apple ID 邮箱。";
            return false;
        }

        var config = AppServices.Config.Config.CalDav;
        config.AppleId = appleId;
        var password = CalDavPasswordBox.Password;
        if (!string.IsNullOrWhiteSpace(password))
        {
            config.AppPassword = password;
        }

        if (string.IsNullOrWhiteSpace(config.AppPassword))
        {
            error = "请填写 iCloud 应用专用密码（已保存过密码时可留空）。";
            return false;
        }

        return true;
    }

    private async void CalDavTest_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = SnapshotCalDav();
        if (!TryApplyCalDavUiToConfig(out var error))
        {
            SetStatus(CalDavStatusText, error, _dangerBrush);
            RestoreCalDav(snapshot);
            return;
        }

        SetCalDavButtonsEnabled(false);
        SetStatus(CalDavStatusText, "正在测试 iCloud 连接…", _secondaryBrush);
        try
        {
            await AppServices.CalDav.TestConnectionAsync();
            SetStatus(CalDavStatusText, "连接成功：Apple ID 与应用专用密码有效。", _successBrush);
        }
        catch (Exception ex)
        {
            AppLog.Error("测试 iCloud 日历连接失败", ex);
            SetStatus(CalDavStatusText, "连接失败：" + ex.Message, _dangerBrush);
        }
        finally
        {
            RestoreCalDav(snapshot);
            SetCalDavButtonsEnabled(true);
        }
    }

    private async void LoadCalendars_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = SnapshotCalDav();
        if (!TryApplyCalDavUiToConfig(out var error))
        {
            SetStatus(CalDavStatusText, error, _dangerBrush);
            RestoreCalDav(snapshot);
            return;
        }

        SetCalDavButtonsEnabled(false);
        SetStatus(CalDavStatusText, "正在读取日历列表…", _secondaryBrush);
        try
        {
            var calendars = await AppServices.CalDav.GetCalendarsAsync();
            RestoreCalDav(snapshot);

            var previousHref = (CalendarComboBox.SelectedItem as CalendarInfo)?.Url;
            CalendarComboBox.Items.Clear();
            foreach (var calendar in calendars)
            {
                CalendarComboBox.Items.Add(calendar);
            }

            if (CalendarComboBox.Items.Count > 0)
            {
                var index = 0;
                if (!string.IsNullOrWhiteSpace(previousHref))
                {
                    for (var i = 0; i < CalendarComboBox.Items.Count; i++)
                    {
                        if (CalendarComboBox.Items[i] is CalendarInfo info &&
                            (info.Url == previousHref || info.Href == previousHref))
                        {
                            index = i;
                            break;
                        }
                    }
                }

                CalendarComboBox.SelectedIndex = index;
                CalendarHintText.Text = "共读取到 " + CalendarComboBox.Items.Count +
                                        " 个日历；选中后点击「保存设置」保存为同步目标。";
                SetStatus(CalDavStatusText, "读取成功：共 " + CalendarComboBox.Items.Count + " 个日历。", _successBrush);
            }
            else
            {
                CalendarHintText.Text = "账号下没有可用的日历。";
                SetStatus(CalDavStatusText, "读取成功，但账号下没有可用日历。", _dangerBrush);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("读取 iCloud 日历列表失败", ex);
            RestoreCalDav(snapshot);
            SetStatus(CalDavStatusText, "读取失败：" + ex.Message, _dangerBrush);
        }
        finally
        {
            SetCalDavButtonsEnabled(true);
        }
    }

    private (string AppleId, string AppPassword) SnapshotCalDav()
    {
        var config = AppServices.Config.Config.CalDav;
        return (config.AppleId, config.AppPassword);
    }

    private static void RestoreCalDav((string AppleId, string AppPassword) snapshot)
    {
        var config = AppServices.Config.Config.CalDav;
        config.AppleId = snapshot.AppleId;
        config.AppPassword = snapshot.AppPassword;
    }

    private void SetCalDavButtonsEnabled(bool enabled)
    {
        CalDavTestButton.IsEnabled = enabled;
        LoadCalendarsButton.IsEnabled = enabled;
    }

    private void CalendarComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CalendarComboBox.SelectedItem is CalendarInfo info)
        {
            CalendarHintText.Text = "当前选择：「" + info.DisplayName + "」；点击「保存设置」保存为同步目标。";
        }
    }

    // ===== 快捷键 =====

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        if (e.Key is Key.Back or Key.Delete)
        {
            _hotkeyGesture = HotkeyGesture.Default;
            HotkeyBox.Text = _hotkeyGesture.DisplayText;
            SetStatus(HotkeyStatusText, "已恢复默认快捷键：" + _hotkeyGesture.DisplayText + "。", _secondaryBrush);
            return;
        }

        // Alt 组合键在 WPF 中表现为 Key.System，真正的按键在 SystemKey 中。
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
            or Key.None or Key.ImeProcessed)
        {
            return;
        }

        var gesture = HotkeyGesture.FromWpfKey(key, Keyboard.Modifiers);
        if (gesture is null)
        {
            SetStatus(HotkeyStatusText, "无法识别该按键，请换一个组合键。", _dangerBrush);
            return;
        }

        if (!gesture.HasModifier)
        {
            SetStatus(HotkeyStatusText, "快捷键至少需要一个修饰键（Ctrl / Alt / Shift / Win）。", _dangerBrush);
            return;
        }

        _hotkeyGesture = gesture;
        HotkeyBox.Text = gesture.DisplayText;
        SetStatus(HotkeyStatusText, "已记录新快捷键：" + gesture.DisplayText + "，点击「保存设置」后生效。", _successBrush);
    }

    // ===== 通用 =====

    private void OpenScreenshots_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppPaths.EnsureDirectories();
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "\"" + AppPaths.ScreenshotsDirectory + "\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("打开截图目录失败", ex);
            MessageBox.Show(Window.GetWindow(this), "打开截图目录失败：" + ex.Message, "AutoAddSchedule",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            Window.GetWindow(this),
            "确定要退出 AutoAddSchedule 吗？" + Environment.NewLine + Environment.NewLine +
            "退出后托盘图标与截图快捷键将不再可用。",
            "退出程序",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            App.ExitApplication();
        }
    }

    // ===== 保存 =====

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(RetentionBox.Text.Trim(), out var retention) || retention < 1 || retention > 1000)
        {
            SetStatus(SaveStatusText, "截图保留数量需为 1-1000 之间的整数。", _dangerBrush);
            RetentionBox.Focus();
            return;
        }

        if (!TryApplyLlmUiToConfig(out var llmError))
        {
            SetStatus(SaveStatusText, llmError, _dangerBrush);
            return;
        }

        var config = AppServices.Config.Config;
        var caldav = config.CalDav;
        caldav.AppleId = AppleIdBox.Text.Trim();
        var password = CalDavPasswordBox.Password;
        if (!string.IsNullOrWhiteSpace(password))
        {
            caldav.AppPassword = password;
        }

        config.Hotkey.Gesture = _hotkeyGesture.DisplayText;
        config.General.AutoStart = AutoStartCheckBox.IsChecked == true;
        config.General.StartMinimizedToTray = StartMinimizedCheckBox.IsChecked == true;
        config.General.ScreenshotRetention = retention;

        if (CalendarComboBox.SelectedItem is CalendarInfo calendar && !string.IsNullOrWhiteSpace(calendar.Url))
        {
            caldav.CalendarHref = calendar.Url;
            caldav.CalendarDisplayName = calendar.DisplayName;
        }

        try
        {
            App.ApplySettings();
        }
        catch (Exception ex)
        {
            AppLog.Error("保存设置失败", ex);
            SetStatus(SaveStatusText, "保存设置失败：" + ex.Message, _dangerBrush);
            return;
        }

        LlmKeyBox.Clear();
        CalDavPasswordBox.Clear();
        UpdateKeyPlaceholders();

        SetStatus(SaveStatusText, "设置已保存并生效。", _successBrush);

        if (!AppServices.Hotkey.IsRegistered)
        {
            SetStatus(
                SaveStatusText,
                "设置已保存，但快捷键注册失败：" + (AppServices.Hotkey.LastError ?? "未知原因") +
                "。请更换快捷键后重新保存。",
                _dangerBrush);
        }
    }
}
