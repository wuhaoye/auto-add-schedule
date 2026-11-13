using System.Text.Json.Serialization;

namespace AutoAddSchedule.Core;

/// <summary>应用总配置。密钥字段不写入 config.json，单独用 DPAPI 加密保存。</summary>
public sealed class AppConfig
{
    public LlmConfig Llm { get; set; } = new();

    public CalDavConfig CalDav { get; set; } = new();

    public HotkeyConfig Hotkey { get; set; } = new();

    public GeneralConfig General { get; set; } = new();

    /// <summary>是否完成过首次配置。</summary>
    public bool FirstRunCompleted { get; set; }
}

public sealed class LlmConfig
{
    public string BaseUrl { get; set; } = "https://api.deepseek.com";

    /// <summary>模型名。默认 deepseek-flash（支持图片输入）。</summary>
    public string Model { get; set; } = "deepseek-flash";

    public int TimeoutSeconds { get; set; } = 120;

    [JsonIgnore]
    public string ApiKey { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(Model);
}

public sealed class CalDavConfig
{
    public string ServerUrl { get; set; } = "https://caldav.icloud.com";

    /// <summary>Apple ID 邮箱（非敏感）。</summary>
    public string AppleId { get; set; } = string.Empty;

    [JsonIgnore]
    public string AppPassword { get; set; } = string.Empty;

    /// <summary>选定的日历集合地址（CalDAV href 或绝对 URL）。</summary>
    public string? CalendarHref { get; set; }

    /// <summary>选定日历的显示名（仅用于界面展示）。</summary>
    public string? CalendarDisplayName { get; set; }

    [JsonIgnore]
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AppleId) &&
        !string.IsNullOrWhiteSpace(AppPassword);
}

public sealed class HotkeyConfig
{
    /// <summary>全局快捷键，格式如 Ctrl+Alt+\ 。</summary>
    public string Gesture { get; set; } = "Ctrl+Alt+\\";
}

public sealed class GeneralConfig
{
    public bool AutoStart { get; set; }

    public bool StartMinimizedToTray { get; set; } = true;

    /// <summary>截图保留数量，默认 60。</summary>
    public int ScreenshotRetention { get; set; } = 60;
}
