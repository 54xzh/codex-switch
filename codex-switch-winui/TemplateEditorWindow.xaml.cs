using System;
using System.Threading.Tasks;
using codex_switch_winui.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace codex_switch_winui;

public sealed partial class TemplateEditorWindow : Window
{
    private readonly ConfigTemplateStore _store = new();

    public TemplateEditorWindow()
    {
        InitializeComponent();
        TrySetWindowSizeAndCenter(1820, 1170);

        OpenAiTemplateBox.Text = _store.LoadOpenAiTemplate();
        ApiKeyTemplateBox.Text = _store.LoadApiKeyTemplate();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _store.SaveOpenAiTemplate(OpenAiTemplateBox.Text);
            _store.SaveApiKeyTemplate(ApiKeyTemplateBox.Text);
            await ShowInfoAsync("成功", "已保存模板。", sender);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("保存失败", ex.Message, sender);
        }
    }

    private async void ResetCurrent_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (TemplatePivot.SelectedIndex == 0)
            {
                _store.ResetOpenAiTemplate();
                OpenAiTemplateBox.Text = _store.LoadOpenAiTemplate();
            }
            else
            {
                _store.ResetApiKeyTemplate();
                ApiKeyTemplateBox.Text = _store.LoadApiKeyTemplate();
            }

            await ShowInfoAsync("成功", "已重置为默认模板。", sender);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("重置失败", ex.Message, sender);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async Task ShowInfoAsync(string title, string message, object? sender = null)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = (Content as FrameworkElement)?.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "确定",
            DefaultButton = ContentDialogButton.Close
        };

        if (dialog.XamlRoot is null)
        {
            return;
        }

        await dialog.ShowAsync();
    }

    private async Task ShowErrorAsync(string title, string message, object? sender = null)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = (Content as FrameworkElement)?.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "确定",
            DefaultButton = ContentDialogButton.Close
        };

        if (dialog.XamlRoot is null)
        {
            return;
        }

        await dialog.ShowAsync();
    }

    private void TrySetWindowSizeAndCenter(int width, int height)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            appWindow.Resize(new SizeInt32(width, height));

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var x = displayArea.WorkArea.X + (displayArea.WorkArea.Width - width) / 2;
            var y = displayArea.WorkArea.Y + (displayArea.WorkArea.Height - height) / 2;
            appWindow.Move(new PointInt32(x, y));
        }
        catch
        {
            // ignore
        }
    }
}
