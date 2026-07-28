using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VelvetTools.Common;

namespace VelvetTools.Modules.Translate;

public partial class TranslateWindow : GlassWindow
{
    private static readonly (string Code, string Name)[] Providers =
    {
        ("openai", "OpenAI 兼容接口"),
        ("deepl", "DeepL"),
        ("baidu", "百度翻译"),
    };

    public TranslateWindow()
    {
        InitializeComponent();
        EscapeAction = EscAction.Close;

        foreach (var (_, name) in Providers) ProviderBox.Items.Add(name);
        foreach (var (_, name) in TranslateService.Languages) LangBox.Items.Add(name);

        var s = App.Services.Settings.Translate;
        ProviderBox.SelectedIndex = Math.Max(0, Array.FindIndex(Providers, p => p.Code == s.Provider));
        LangBox.SelectedIndex = Math.Max(0, Array.FindIndex(TranslateService.Languages, l => l.Code == s.TargetLang));

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                _ = RunTranslateAsync();
            }
        };
    }

    /// <summary>打开翻译窗口；autoTranslate 时立即执行。</summary>
    public static void Open(string sourceText, bool autoTranslate = false)
    {
        var w = new TranslateWindow();
        w.SourceBox.Text = sourceText;
        w.Show();
        w.Activate();
        if (autoTranslate && !string.IsNullOrWhiteSpace(sourceText))
            _ = w.RunTranslateAsync();
    }

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        SavePrefs();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(ResultBox.Text);
            Toast.Show("已复制译文");
        }
        catch { }
    }

    private async void OnTranslateClick(object sender, RoutedEventArgs e) => await RunTranslateAsync();

    private async Task RunTranslateAsync()
    {
        string text = SourceBox.Text;
        if (string.IsNullOrWhiteSpace(text)) return;

        SavePrefs();
        TranslateBtn.IsEnabled = false;
        StatusText.Text = "翻译中…";
        try
        {
            string lang = TranslateService.Languages[Math.Max(0, LangBox.SelectedIndex)].Code;
            string result = await App.Services.Translate.TranslateAsync(text, lang);
            ResultBox.Text = result;
            CopyBtn.Visibility = Visibility.Visible;
            StatusText.Text = $"完成 · {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "";
            ResultBox.Text = "";
            Logger.Error("翻译失败", ex);
            Toast.Show("翻译失败：" + ex.Message, 4000);
        }
        finally
        {
            TranslateBtn.IsEnabled = true;
        }
    }

    private void SavePrefs()
    {
        var s = App.Services.Settings.Translate;
        s.Provider = Providers[Math.Max(0, ProviderBox.SelectedIndex)].Code;
        s.TargetLang = TranslateService.Languages[Math.Max(0, LangBox.SelectedIndex)].Code;
        App.Services.Settings.Save();
    }
}
