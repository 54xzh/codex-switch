using System;
using codex_switch_winui.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace codex_switch_winui.Dialogs;

public sealed partial class SettingsDialog : ContentDialog
{
    private readonly WslEnvironmentService _wsl = new();
    private WslEnvironmentInfo? _defaultEnvironment;

    public string? WslDistroName { get; private set; }
    public string? WslUserName { get; private set; }

    public SettingsDialog(XamlRoot xamlRoot, string? wslDistroName, string? wslUserName)
    {
        InitializeComponent();

        XamlRoot = xamlRoot;
        Title = "设置";
        PrimaryButtonText = "保存";
        CloseButtonText = "取消";
        DefaultButton = ContentDialogButton.Primary;
        PrimaryButtonClick += SettingsDialog_PrimaryButtonClick;

        DistroNameBox.Text = wslDistroName ?? string.Empty;
        UserNameBox.Text = wslUserName ?? string.Empty;

        LoadDetectedDefaults();
        UpdatePreview();
    }

    private void LoadDetectedDefaults()
    {
        if (_wsl.TryGetDefaultEnvironment(out var info, out var errorMessage) && info is not null)
        {
            _defaultEnvironment = info;
            DetectedDefaultsTextBlock.Text = $"默认值：{info.DistroName} / {info.UserName}";

            if (string.IsNullOrWhiteSpace(DistroNameBox.Text))
            {
                DistroNameBox.PlaceholderText = info.DistroName;
            }

            if (string.IsNullOrWhiteSpace(UserNameBox.Text))
            {
                UserNameBox.PlaceholderText = info.UserName;
            }

            return;
        }

        DetectedDefaultsTextBlock.Text = $"默认值读取失败：{errorMessage}";
    }

    private void UseDetectedDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (_defaultEnvironment is null)
        {
            ShowValidationError("当前无法读取默认 WSL 信息，请手动填写。");
            return;
        }

        DistroNameBox.Text = _defaultEnvironment.DistroName;
        UserNameBox.Text = _defaultEnvironment.UserName;
        ErrorBar.IsOpen = false;
        UpdatePreview();
    }

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void SettingsDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var distroName = NormalizeOptionalValue(DistroNameBox.Text);
        var userName = NormalizeOptionalValue(UserNameBox.Text);

        if (ContainsPathSeparator(distroName))
        {
            args.Cancel = true;
            ShowValidationError("发行版名称里不要包含 / 或 \\。");
            return;
        }

        if (ContainsPathSeparator(userName))
        {
            args.Cancel = true;
            ShowValidationError("用户名里不要包含 / 或 \\。");
            return;
        }

        WslDistroName = distroName;
        WslUserName = userName;
        ErrorBar.IsOpen = false;
    }

    private void UpdatePreview()
    {
        var distroName = NormalizeOptionalValue(DistroNameBox.Text) ?? _defaultEnvironment?.DistroName;
        var userName = NormalizeOptionalValue(UserNameBox.Text) ?? _defaultEnvironment?.UserName;

        if (string.IsNullOrWhiteSpace(distroName) || string.IsNullOrWhiteSpace(userName))
        {
            PreviewTextBlock.Text = "请填写发行版和用户名，或先读取默认值。";
            return;
        }

        var linuxPath = $"/home/{userName}/.codex";
        var windowsPath = _wsl.ToWindowsPath(distroName, linuxPath);
        PreviewTextBlock.Text = $"Linux 路径：{linuxPath}\nWindows 访问路径：{windowsPath}";
    }

    private void ShowValidationError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }

    private static bool ContainsPathSeparator(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (value.Contains('/', StringComparison.Ordinal) || value.Contains('\\', StringComparison.Ordinal));

    private static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
