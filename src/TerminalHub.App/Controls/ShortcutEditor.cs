using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace TerminalHub.App.Controls;

/// <summary>Records an app gesture without sending the recording keys to a terminal.</summary>
public sealed class ShortcutEditor : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);
    public ShortcutEditor()
    {
        IsReadOnly = true;
        this.Bind(WatermarkProperty, Localization.UiText.Binding("点击后按快捷键"));
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Back or Key.Delete)
            SetCurrentValue(TextProperty, "");
        else if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = false;
            base.OnKeyDown(e);
        }
        else if (e.Key == Key.Escape) TopLevel.GetTopLevel(this)?.Focus();
        else
        {
            var text = new KeyGesture(e.Key, e.KeyModifiers).ToString();
            if (e.Key is >= Key.D0 and <= Key.D9) text = text.Replace(e.Key.ToString(), ((int)e.Key - (int)Key.D0).ToString());
            SetCurrentValue(TextProperty, text);
        }
    }
}
