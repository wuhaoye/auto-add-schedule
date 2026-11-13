using System.IO;
using System.Text.Json;

namespace AutoAddSchedule.Core;

/// <summary>
/// 配置读写：config.json 保存非敏感项，secrets.dat 保存 DPAPI 加密后的密钥。
/// 首次启动会从 .env 导入缺失的密钥，不会覆盖界面里已填的值。
/// </summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public AppConfig Config { get; private set; } = new();

    /// <summary>加载过程中的警告（如配置文件损坏），供界面提示。</summary>
    public string? LoadWarning { get; private set; }

    public event EventHandler? Changed;

    public void Load()
    {
        AppPaths.EnsureDirectories();
        Config = new AppConfig();

        if (File.Exists(AppPaths.ConfigFilePath))
        {
            try
            {
                var json = File.ReadAllText(AppPaths.ConfigFilePath);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (loaded is not null)
                {
                    Config = loaded;
                }
            }
            catch (Exception ex)
            {
                LoadWarning = "配置文件损坏，已使用默认配置：" + ex.Message;
                AppLog.Error("读取 config.json 失败", ex);
                Config = new AppConfig();
            }
        }

        LoadSecrets();
        ApplyEnvDefaults();
    }

    private void LoadSecrets()
    {
        if (!File.Exists(AppPaths.SecretsFilePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(AppPaths.SecretsFilePath);
            var payload = JsonSerializer.Deserialize<SecretsPayload>(json, JsonOptions);
            if (payload is null)
            {
                return;
            }

            Config.Llm.ApiKey = SecretProtector.Unprotect(payload.ApiKey);
            Config.CalDav.AppPassword = SecretProtector.Unprotect(payload.AppPassword);
        }
        catch (Exception ex)
        {
            AppLog.Error("读取 secrets.dat 失败", ex);
        }
    }

    private void ApplyEnvDefaults()
    {
        var env = EnvFile.LoadDefault();
        if (env.IsEmpty)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Config.Llm.ApiKey))
        {
            var value = env.Get("MODELKEY") ?? env.Get("DEEPSEEK_API_KEY");
            if (!string.IsNullOrWhiteSpace(value))
            {
                Config.Llm.ApiKey = value;
            }
        }

        if (string.IsNullOrWhiteSpace(Config.CalDav.AppleId))
        {
            var value = env.Get("APPLEID") ?? env.Get("APPLE_ID");
            if (!string.IsNullOrWhiteSpace(value))
            {
                Config.CalDav.AppleId = value;
            }
        }

        if (string.IsNullOrWhiteSpace(Config.CalDav.AppPassword))
        {
            var value = env.Get("APPLEKEY") ?? env.Get("APPLE_APP_PASSWORD");
            if (!string.IsNullOrWhiteSpace(value))
            {
                Config.CalDav.AppPassword = value;
            }
        }

        var baseUrl = env.Get("MODELBASE") ?? env.Get("MODEL_BASE_URL");
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            Config.Llm.BaseUrl = baseUrl;
        }

        var model = env.Get("MODEL") ?? env.Get("MODEL_NAME");
        if (!string.IsNullOrWhiteSpace(model))
        {
            Config.Llm.Model = model;
        }
    }

    public void Save()
    {
        AppPaths.EnsureDirectories();
        try
        {
            var configJson = JsonSerializer.Serialize(Config, JsonOptions);
            WriteAtomic(AppPaths.ConfigFilePath, configJson);

            var payload = new SecretsPayload
            {
                ApiKey = SecretProtector.Protect(Config.Llm.ApiKey),
                AppPassword = SecretProtector.Protect(Config.CalDav.AppPassword)
            };
            var secretsJson = JsonSerializer.Serialize(payload, JsonOptions);
            WriteAtomic(AppPaths.SecretsFilePath, secretsJson);

            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppLog.Error("保存配置失败", ex);
            throw;
        }
    }

    private static void WriteAtomic(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }

    private sealed class SecretsPayload
    {
        public string ApiKey { get; set; } = string.Empty;

        public string AppPassword { get; set; } = string.Empty;
    }
}
