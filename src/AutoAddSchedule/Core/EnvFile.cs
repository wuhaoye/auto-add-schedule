using System.IO;

namespace AutoAddSchedule.Core;

/// <summary>
/// 读取仓库 / 程序目录附近的 .env 文件。格式为每行 KEY=VALUE，支持 # 注释。
/// 该文件只用于首次导入密钥，已被 .gitignore 忽略。
/// </summary>
public sealed class EnvFile
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty => _values.Count == 0;

    public string? SourcePath { get; private set; }

    public string? Get(string key)
    {
        return _values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }

    public static EnvFile LoadDefault() => Load(AppPaths.FindEnvFile());

    public static EnvFile Load(string? path)
    {
        var result = new EnvFile();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return result;
        }

        result.SourcePath = path;
        try
        {
            var lines = File.ReadAllLines(path);
            foreach (var raw in lines)
            {
                var line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                var index = line.IndexOf('=');
                if (index <= 0)
                {
                    continue;
                }

                var key = line[..index].Trim();
                var value = line[(index + 1)..].Trim();
                if (value.Length >= 2 &&
                    ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                {
                    value = value[1..^1];
                }

                if (key.Length > 0)
                {
                    result._values[key] = value;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("读取 .env 失败：" + ex.Message);
        }

        return result;
    }
}
