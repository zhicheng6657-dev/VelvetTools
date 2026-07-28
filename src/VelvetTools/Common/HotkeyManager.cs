using VelvetTools.Common.Interop;
using System.Windows.Input;

namespace VelvetTools.Common;

/// <summary>
/// 全局热键管理：解析 "Ctrl+Alt+A" 形式的字符串并注册系统热键。
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly MessageWindow _window;
    private readonly Dictionary<int, Action> _actions = new();
    private readonly Dictionary<string, Registration> _registrations = new(StringComparer.OrdinalIgnoreCase);
    private int _nextId = 0xF00;

    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
    private sealed record Registration(int Id, uint Modifiers, uint VirtualKey, string Gesture);

    public HotkeyManager(MessageWindow window)
    {
        _window = window;
        _window.AddHook((msg, wParam, _) =>
        {
            if (msg != Native.WM_HOTKEY) return false;
            if (_actions.TryGetValue(wParam.ToInt32(), out var action))
            {
                action();
                return true;
            }
            return false;
        });
    }

    /// <summary>
    /// 原子地注册命名热键。新组合会先在旧组合仍有效时试注册；
    /// 解析失败或发生冲突时保留旧热键，不会把当前可用的快捷键弄丢。
    /// </summary>
    public string? Register(string name, string? gesture, Action action)
    {
        gesture = gesture?.Trim();
        if (string.IsNullOrWhiteSpace(gesture))
        {
            Unregister(name); // 留空是用户明确停用
            return null;
        }

        if (!TryParse(gesture, out uint mods, out uint vk))
            return $"无法解析热键 \"{gesture}\"";

        if (_registrations.TryGetValue(name, out var old)
            && old.Modifiers == mods && old.VirtualKey == vk)
        {
            _actions[old.Id] = action;
            _registrations[name] = old with { Gesture = gesture };
            return null;
        }

        // 先占住新组合；若失败，旧组合从未被反注册。
        int id = _nextId++;
        if (!Native.RegisterHotKey(_window.Handle, id, mods | MOD_NOREPEAT, vk))
            return $"热键 \"{gesture}\" 已被其他程序或本应用的其他功能占用";

        if (old is not null)
        {
            Native.UnregisterHotKey(_window.Handle, old.Id);
            _actions.Remove(old.Id);
        }

        _actions[id] = action;
        _registrations[name] = new Registration(id, mods, vk, gesture);
        return null;
    }

    public void Unregister(string name)
    {
        if (_registrations.Remove(name, out var registration))
        {
            Native.UnregisterHotKey(_window.Handle, registration.Id);
            _actions.Remove(registration.Id);
        }
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys.ToList())
            Native.UnregisterHotKey(_window.Handle, id);
        _actions.Clear();
        _registrations.Clear();
    }

    public void Dispose() => UnregisterAll();

    public static bool TryParse(string gesture, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        foreach (var raw in gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= MOD_CONTROL; break;
                case "alt": mods |= MOD_ALT; break;
                case "shift": mods |= MOD_SHIFT; break;
                case "win" or "windows": mods |= MOD_WIN; break;
                default:
                    if (vk != 0) return false; // 出现两个主键
                    vk = KeyNameToVk(raw);
                    if (vk == 0) return false;
                    break;
            }
        }
        return vk != 0;
    }

    /// <summary>把 WPF 实际键盘输入格式化成持久化和 RegisterHotKey 共用的标准字符串。</summary>
    public static bool TryFormatGesture(ModifierKeys modifiers, Key key, out string gesture)
    {
        gesture = "";
        string? keyName = KeyToName(key);
        if (keyName is null) return false;

        var parts = new List<string>(5);
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(keyName);
        gesture = string.Join("+", parts);
        return TryParse(gesture, out _, out _);
    }

    private static string? KeyToName(Key key)
    {
        if (key is >= Key.A and <= Key.Z) return key.ToString();
        if (key is >= Key.D0 and <= Key.D9) return ((int)key - (int)Key.D0).ToString();
        if (key is >= Key.F1 and <= Key.F24) return key.ToString();
        if (key is >= Key.NumPad0 and <= Key.NumPad9) return "Num" + ((int)key - (int)Key.NumPad0);

        return key switch
        {
            Key.Space => "Space",
            Key.Enter => "Enter",
            Key.Tab => "Tab",
            Key.Escape => "Esc",
            Key.Back => "Backspace",
            Key.Insert => "Insert",
            Key.Delete => "Delete",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Left => "Left",
            Key.Up => "Up",
            Key.Right => "Right",
            Key.Down => "Down",
            Key.PrintScreen or Key.Snapshot => "PrintScreen",
            Key.Oem3 => "`",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPipe => "\\",
            _ => null,
        };
    }

    private static uint KeyNameToVk(string name)
    {
        name = name.Trim();
        if (name.Length == 1)
        {
            char c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
        }
        return name.ToLowerInvariant() switch
        {
            "space" or "空格" => 0x20,
            "enter" or "return" => 0x0D,
            "tab" => 0x09,
            "esc" or "escape" => 0x1B,
            "backspace" => 0x08,
            "insert" => 0x2D,
            "delete" or "del" => 0x2E,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" or "pgup" => 0x21,
            "pagedown" or "pgdn" => 0x22,
            "left" => 0x25, "up" => 0x26, "right" => 0x27, "down" => 0x28,
            "printscreen" or "prtsc" or "prtscn" => 0x2C,
            "`" or "tilde" or "波浪号" => 0xC0,
            "-" or "minus" => 0xBD,
            "=" or "plus" => 0xBB,
            "," or "comma" => 0xBC,
            "." or "period" => 0xBE,
            "/" or "slash" => 0xBF,
            ";" or "semicolon" => 0xBA,
            "'" or "quote" => 0xDE,
            "[" => 0xDB, "]" => 0xDD, "\\" => 0xDC,
            _ when name.StartsWith("num", StringComparison.OrdinalIgnoreCase)
                   && name.Length == 4 && char.IsDigit(name[3])
                => (uint)(0x60 + (name[3] - '0')),
            _ when name.Length is 2 or 3 && (name[0] is 'f' or 'F') && int.TryParse(name[1..], out int f) && f is >= 1 and <= 24
                => (uint)(0x70 + f - 1), // F1..F24
            _ => 0,
        };
    }
}
