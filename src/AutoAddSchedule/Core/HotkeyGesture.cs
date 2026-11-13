using System.Text;
using System.Windows.Input;

namespace AutoAddSchedule.Core;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8
}

/// <summary>全局快捷键描述与解析，例如 Ctrl+Alt+\ 。</summary>
public sealed class HotkeyGesture
{
    /// <summary>虚拟键码到显示名的映射。</summary>
    private static readonly Dictionary<int, string> VirtualKeyNames = new()
    {
        [0x08] = "Backspace",
        [0x09] = "Tab",
        [0x0D] = "Enter",
        [0x13] = "Pause",
        [0x14] = "CapsLock",
        [0x1B] = "Esc",
        [0x20] = "Space",
        [0x21] = "PageUp",
        [0x22] = "PageDown",
        [0x23] = "End",
        [0x24] = "Home",
        [0x25] = "Left",
        [0x26] = "Up",
        [0x27] = "Right",
        [0x28] = "Down",
        [0x2C] = "PrintScreen",
        [0x2D] = "Insert",
        [0x2E] = "Delete",
        [0x5B] = "Win",
        [0x5C] = "Win",
        [0xBA] = ";",
        [0xBB] = "=",
        [0xBC] = ",",
        [0xBD] = "-",
        [0xBE] = ".",
        [0xBF] = "/",
        [0xC0] = "\u0060",
        [0xDB] = "[",
        [0xDC] = "\\",
        [0xDD] = "]",
        [0xDE] = "'"
    };

    /// <summary>显示名到虚拟键码的映射（用于解析）。</summary>
    private static readonly Dictionary<string, int> NameToVirtualKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Backspace"] = 0x08,
        ["Tab"] = 0x09,
        ["Enter"] = 0x0D,
        ["Pause"] = 0x13,
        ["CapsLock"] = 0x14,
        ["Esc"] = 0x1B,
        ["Escape"] = 0x1B,
        ["Space"] = 0x20,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["End"] = 0x23,
        ["Home"] = 0x24,
        ["Left"] = 0x25,
        ["Up"] = 0x26,
        ["Right"] = 0x27,
        ["Down"] = 0x28,
        ["PrintScreen"] = 0x2C,
        ["Insert"] = 0x2D,
        ["Delete"] = 0x2E,
        [";"] = 0xBA,
        ["="] = 0xBB,
        [","] = 0xBC,
        ["-"] = 0xBD,
        ["."] = 0xBE,
        ["/"] = 0xBF,
        ["\u0060"] = 0xC0,
        ["["] = 0xDB,
        ["\\"] = 0xDC,
        ["]"] = 0xDD,
        ["'"] = 0xDE
    };

    public HotkeyModifiers Modifiers { get; init; }

    public int VirtualKey { get; init; }

    public string KeyName { get; init; } = string.Empty;

    public static HotkeyGesture Default => Parse("Ctrl+Alt+\\") ?? new HotkeyGesture
    {
        Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt,
        VirtualKey = 0xDC,
        KeyName = "\\"
    };

    public string DisplayText
    {
        get
        {
            var builder = new StringBuilder();
            if (Modifiers.HasFlag(HotkeyModifiers.Control))
            {
                builder.Append("Ctrl+");
            }

            if (Modifiers.HasFlag(HotkeyModifiers.Alt))
            {
                builder.Append("Alt+");
            }

            if (Modifiers.HasFlag(HotkeyModifiers.Shift))
            {
                builder.Append("Shift+");
            }

            if (Modifiers.HasFlag(HotkeyModifiers.Win))
            {
                builder.Append("Win+");
            }

            builder.Append(KeyName);
            return builder.ToString();
        }
    }

    public bool HasModifier => Modifiers != HotkeyModifiers.None;

    public override string ToString() => DisplayText;

    public static HotkeyGesture? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = HotkeyModifiers.None;
        var keyToken = string.Empty;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= HotkeyModifiers.Control;
                    break;
                case "alt":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "shift":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "win":
                case "windows":
                    modifiers |= HotkeyModifiers.Win;
                    break;
                case "":
                    // 兼容以 + 结尾的手写配置（键本身是 +）。
                    keyToken = "+";
                    break;
                default:
                    keyToken = part;
                    break;
            }
        }

        if (keyToken.Length == 0)
        {
            return null;
        }

        var virtualKey = ResolveVirtualKey(keyToken);
        if (virtualKey == 0)
        {
            return null;
        }

        return new HotkeyGesture
        {
            Modifiers = modifiers,
            VirtualKey = virtualKey,
            KeyName = NormalizeKeyName(virtualKey, keyToken)
        };
    }

    public static HotkeyGesture? FromWpfKey(Key key, ModifierKeys modifierKeys)
    {
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            return null;
        }

        var modifiers = HotkeyModifiers.None;
        if (modifierKeys.HasFlag(ModifierKeys.Control))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (modifierKeys.HasFlag(ModifierKeys.Alt))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (modifierKeys.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (modifierKeys.HasFlag(ModifierKeys.Windows))
        {
            modifiers |= HotkeyModifiers.Win;
        }

        return new HotkeyGesture
        {
            Modifiers = modifiers,
            VirtualKey = virtualKey,
            KeyName = NormalizeKeyName(virtualKey, key.ToString())
        };
    }

    private static int ResolveVirtualKey(string token)
    {
        if (NameToVirtualKey.TryGetValue(token, out var named))
        {
            return named;
        }

        if (token.Length == 1)
        {
            var ch = char.ToUpperInvariant(token[0]);
            if (ch is >= 'A' and <= 'Z')
            {
                return ch;
            }

            if (ch is >= '0' and <= '9')
            {
                return ch;
            }

            if (token[0] == '+')
            {
                return 0xBB;
            }
        }

        if (token.Length is 2 or 3 && (token[0] is 'F' or 'f') && int.TryParse(token[1..], out var functionKey) &&
            functionKey is >= 1 and <= 24)
        {
            return 0x6F + functionKey;
        }

        return 0;
    }

    private static string NormalizeKeyName(int virtualKey, string fallback)
    {
        if (VirtualKeyNames.TryGetValue(virtualKey, out var name))
        {
            return name;
        }

        if (virtualKey is >= 0x30 and <= 0x39)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            return "F" + (virtualKey - 0x6F);
        }

        return fallback;
    }
}
