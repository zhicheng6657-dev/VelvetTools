using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VelvetTools.Common;

namespace VelvetTools.Settings;

/// <summary>
/// 点击后监听真实键盘输入的快捷键控件。它不接受自由文本，因此不会保存拼写错误；
/// Esc 取消本次录入，未按修饰键的 Backspace 清除快捷键。
/// </summary>
public sealed class HotkeyCaptureBox : Button
{
    private bool _capturing;
    private string _gesture = "";
    private string _beforeCapture = "";

    public Func<string, bool>? CanCommit { get; set; }

    public string Gesture
    {
        get => _gesture;
        set
        {
            _gesture = value?.Trim() ?? "";
            if (!_capturing) UpdateContent();
        }
    }

    protected override void OnClick()
    {
        base.OnClick();
        BeginCapture();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_capturing)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys modifiers = Keyboard.Modifiers;

        if (key == Key.Escape)
        {
            CancelCapture();
            return;
        }

        if (key == Key.Back && modifiers == ModifierKeys.None)
        {
            Commit("");
            return;
        }

        if (IsModifierKey(key))
        {
            Content = FormatModifierPreview(modifiers);
            return;
        }

        if (!HotkeyManager.TryFormatGesture(modifiers, key, out string gesture))
        {
            Content = "不支持该按键，请重试";
            return;
        }

        Commit(gesture);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        if (_capturing) CancelCapture();
        base.OnLostKeyboardFocus(e);
    }

    private void BeginCapture()
    {
        if (_capturing) return;
        _capturing = true;
        _beforeCapture = _gesture;
        Content = "请按下快捷键…";
        BorderThickness = new Thickness(2);
        SetResourceReference(BorderBrushProperty, "AccentBrush");
        Focus();
        Keyboard.Focus(this);
    }

    private void Commit(string gesture)
    {
        if (CanCommit?.Invoke(gesture) == false)
        {
            _gesture = _beforeCapture;
            FinishCapture();
            return;
        }

        _gesture = gesture;
        FinishCapture();
    }

    private void CancelCapture()
    {
        _gesture = _beforeCapture;
        FinishCapture();
    }

    private void FinishCapture()
    {
        _capturing = false;
        ClearValue(BorderBrushProperty);
        ClearValue(BorderThicknessProperty);
        UpdateContent();
    }

    private void UpdateContent()
    {
        Content = string.IsNullOrWhiteSpace(_gesture) ? "未设置（点击录入）" : _gesture;
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin;

    private static string FormatModifierPreview(ModifierKeys modifiers)
    {
        var parts = new List<string>(4);
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        return parts.Count == 0 ? "请按下快捷键…" : string.Join("+", parts) + "+…";
    }
}
