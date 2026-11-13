using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using AutoAddSchedule.Core;

namespace AutoAddSchedule.Services;

/// <summary>基于 Win32 RegisterHotKey 的全局快捷键，使用隐藏消息窗口接收 WM_HOTKEY。</summary>
public sealed class HotkeyService : IHotkeyService
{
    private const int HotkeyId = 0x5343;
    private HwndSource? _source;
    private bool _registered;
    private bool _disposed;

    public event EventHandler? Pressed;

    public bool IsRegistered => _registered;

    public string? LastError { get; private set; }

    public bool Register(HotkeyGesture gesture)
    {
        if (_disposed)
        {
            return false;
        }

        Unregister();
        EnsureWindow();

        var success = NativeMethods.RegisterHotKey(
            _source!.Handle,
            HotkeyId,
            (uint)gesture.Modifiers,
            (uint)gesture.VirtualKey);

        if (!success)
        {
            var code = Marshal.GetLastWin32Error();
            LastError = "快捷键 " + gesture.DisplayText + " 注册失败：" +
                        new Win32Exception(code).Message + "（可能已被其他程序占用）";
            AppLog.Warn(LastError);
            return false;
        }

        _registered = true;
        LastError = null;
        AppLog.Info("已注册全局快捷键：" + gesture.DisplayText);
        return true;
    }

    public void Unregister()
    {
        if (!_registered || _source is null)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    private void EnsureWindow()
    {
        if (_source is not null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("AutoAddScheduleHotkeyWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && wParam.ToInt64() == HotkeyId)
        {
            handled = true;
            try
            {
                Pressed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                AppLog.Error("快捷键处理异常", ex);
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unregister();
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
    }
}
