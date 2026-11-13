using System.IO;
using AutoAddSchedule.Core;
using Microsoft.Win32;

namespace AutoAddSchedule.Services;

/// <summary>通过 HKCU\Software\Microsoft\Windows\CurrentVersion\Run 实现开机自启。</summary>
public sealed class AutoStartService : IAutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AutoAddSchedule";

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                var value = key?.GetValue(ValueName) as string;
                return !string.IsNullOrWhiteSpace(value);
            }
            catch (Exception ex)
            {
                AppLog.Warn("读取开机自启状态失败：" + ex.Message);
                return false;
            }
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null)
            {
                return;
            }

            if (enabled)
            {
                key.SetValue(ValueName, "\"" + ResolveExePath() + "\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("设置开机自启失败", ex);
            throw new InvalidOperationException("设置开机自启失败：" + ex.Message, ex);
        }
    }

    private static string ResolveExePath()
    {
        var appHost = Path.Combine(AppPaths.InstallDirectory, "AutoAddSchedule.exe");
        if (File.Exists(appHost))
        {
            return appHost;
        }

        return Environment.ProcessPath ?? appHost;
    }
}
