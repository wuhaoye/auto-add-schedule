using AutoAddSchedule.Core;

namespace AutoAddSchedule.Services;

public interface IHotkeyService : IDisposable
{
    event EventHandler? Pressed;

    bool IsRegistered { get; }

    /// <summary>最近一次注册失败的原因（中文）。</summary>
    string? LastError { get; }

    /// <summary>注册全局快捷键；失败返回 false 并填充 LastError。</summary>
    bool Register(HotkeyGesture gesture);

    void Unregister();
}
