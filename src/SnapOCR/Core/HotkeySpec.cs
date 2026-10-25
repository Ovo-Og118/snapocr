using System;
using System.Windows.Forms;

namespace SnapOcr.Core;

/// <summary>可解析的全局快捷键,例如 "Ctrl+Alt+O"。</summary>
internal readonly record struct HotkeySpec(Keys Key, uint Modifiers, string Text)
{
    public static HotkeySpec Default => new(Keys.O, NativeInterop.ModControl | NativeInterop.ModAlt, "Ctrl+Alt+O");

    public static bool TryParse(string? value, out HotkeySpec spec, out string? error)
    {
        spec = Default;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "快捷键不能为空。";
            return false;
        }

        var parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            error = "快捷键至少需要一个修饰键,例如 Ctrl+Alt+O。";
            return false;
        }

        uint mods = 0;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                case "ctl":
                    mods |= NativeInterop.ModControl;
                    break;
                case "alt":
                    mods |= NativeInterop.ModAlt;
                    break;
                case "shift":
                    mods |= NativeInterop.ModShift;
                    break;
                case "win":
                case "windows":
                case "super":
                    mods |= NativeInterop.ModWin;
                    break;
                default:
                    error = $"无法识别的修饰键:{parts[i]}";
                    return false;
            }
        }

        if (mods == 0)
        {
            error = "快捷键至少需要一个修饰键。";
            return false;
        }

        var keyText = parts[^1];
        Keys key;
        if (keyText.Length == 1 && char.IsLetterOrDigit(keyText[0]))
        {
            key = (Keys)char.ToUpperInvariant(keyText[0]);
        }
        else if (!Enum.TryParse(keyText, ignoreCase: true, out key))
        {
            error = $"无法识别的按键:{keyText}";
            return false;
        }

        spec = new HotkeySpec(key, mods, Describe(mods, key));
        return true;
    }

    private static string Describe(uint mods, Keys key)
    {
        var text = string.Empty;
        if ((mods & NativeInterop.ModControl) != 0) text += "Ctrl+";
        if ((mods & NativeInterop.ModAlt) != 0) text += "Alt+";
        if ((mods & NativeInterop.ModShift) != 0) text += "Shift+";
        if ((mods & NativeInterop.ModWin) != 0) text += "Win+";
        return text + key;
    }
}
