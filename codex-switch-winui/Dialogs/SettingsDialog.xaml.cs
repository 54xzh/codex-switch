using System;
using System.Threading.Tasks;
using codex_switch_winui.Models;
using codex_switch_winui.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace codex_switch_winui.Dialogs;

public sealed partial class SettingsDialog : ContentDialog
{
    private readonly WslEnvironmentService _wsl = new();
    private WslEnvironmentInfo? _defaultEnvironment;
    private bool _isRefreshingDetectedDefaults;

    public string ApiKeyProviderName { get; private set; } = ProfileDatabase.DefaultApiKeyProviderName;
    public string? WslDistroName { get; private set; }
    public string? WslUserName { get; private set; }
    public WslEnvironmentInfo? RefreshedDetectedEnvironment { get; private set; }
    public string? RefreshedDetectedEnvironmentErrorMessage { get; private set; }

    public SettingsDialog(
        XamlRoot xamlRoot,
        string? apiKeyProviderName,
        string? wslDistroName,
        string? wslUserName,
        WslEnvironmentInfo? cachedDefaultEnvironment,
        string? cachedDefaultErrorMessage)
    {
        InitializeComponent();

        XamlRoot = xamlRoot;
        Title = "设置";
        PrimaryButtonText = "保存";
        CloseButtonText = "取消";
        DefaultButton = ContentDialogButton.Primary;
        PrimaryButtonClick += SettingsDialog_PrimaryButtonClick;
        Opened += SettingsDialog_Opened;

        DistroNameBox.Text = wslDistroName ?? string.Empty;
        UserNameBox.Text = wslUserName ?? string.Empty;
        ProviderNameBox.Text = GetEffectiveApiKeyProviderName(apiKeyProviderName);

        ApplyDetectedDefaults(cachedDefaultEnvironment, cachedDefaultErrorMessage);
        UpdatePreview();
    }

    private async void SettingsDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args) =>
        await RefreshDetectedDefaultsAsync(fillInputBoxes: false);

    private async void UseDetectedDefaults_Click(object sender, RoutedEventArgs e)
    {
        ErrorBar.IsOpen = false;

        if (_defaultEnvironment is null)
        {
            await RefreshDetectedDefaultsAsync(fillInputBoxes: true);

            if (_defaultEnvironment is null)
            {
                ShowValidationError("当前无法读取默认 WSL 信息，请手动填写。");
            }

            return;
        }

        FillInputBoxesFromDetectedDefaults();
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

        ApiKeyProviderName = GetEffectiveApiKeyProviderName(ProviderNameBox.Text);
        WslDistroName = distroName;
        WslUserName = userName;
        ErrorBar.IsOpen = false;
    }

    private async Task RefreshDetectedDefaultsAsync(bool fillInputBoxes)
    {
        if (_isRefreshingDetectedDefaults)
        {
            return;
        }

        _isRefreshingDetectedDefaults = true;
        DetectedDefaultsTextBlock.Text = "正在刷新默认 WSL 信息...";

        try
        {
            var result = await Task.Run(() =>
            {
                var success = _wsl.TryRefreshDefaultEnvironment(out var info, out var errorMessage);
                return new RefreshDetectedDefaultsResult(success, info, errorMessage);
            });

            RefreshedDetectedEnvironment = result.Info;
            RefreshedDetectedEnvironmentErrorMessage = result.ErrorMessage;

            ApplyDetectedDefaults(result.Info, result.ErrorMessage);

            if (fillInputBoxes && result.Info is not null)
            {
                FillInputBoxesFromDetectedDefaults();
            }
        }
        finally
        {
            _isRefreshingDetectedDefaults = false;
            UpdatePreview();
        }
    }

    private void ApplyDetectedDefaults(WslEnvironmentInfo? info, string? errorMessage)
    {
        if (info is not null)
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

        if (_defaultEnvironment is not null)
        {
            DetectedDefaultsTextBlock.Text = string.IsNullOrWhiteSpace(errorMessage)
                ? $"默认值：{_defaultEnvironment.DistroName} / {_defaultEnvironment.UserName}"
                : $"已保留缓存默认值，后台刷新失败：{errorMessage}";
            return;
        }

        DetectedDefaultsTextBlock.Text = string.IsNullOrWhiteSpace(errorMessage)
            ? "尚未读取到默认 WSL 信息。"
            : $"默认值读取失败：{errorMessage}";
    }

    private void FillInputBoxesFromDetectedDefaults()
    {
        if (_defaultEnvironment is null)
        {
            return;
        }

        DistroNameBox.Text = _defaultEnvironment.DistroName;
        UserNameBox.Text = _defaultEnvironment.UserName;
        ErrorBar.IsOpen = false;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var providerName = GetEffectiveApiKeyProviderName(ProviderNameBox.Text);
        var distroName = NormalizeOptionalValue(DistroNameBox.Text) ?? _defaultEnvironment?.DistroName;
        var userName = NormalizeOptionalValue(UserNameBox.Text) ?? _defaultEnvironment?.UserName;

        if (string.IsNullOrWhiteSpace(distroName) || string.IsNullOrWhiteSpace(userName))
        {
            PreviewTextBlock.Text = $"API Key 提供商名：{providerName}\n请填写发行版和用户名，或等待默认值刷新完成。";
            return;
        }

        var linuxPath = $"/home/{userName}/.codex";
        var windowsPath = _wsl.ToWindowsPath(distroName, linuxPath);
        PreviewTextBlock.Text = $"API Key 提供商名：{providerName}\nLinux 路径：{linuxPath}\nWindows 访问路径：{windowsPath}";
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

    private static string GetEffectiveApiKeyProviderName(string? value) =>
        NormalizeOptionalValue(value) ?? ProfileDatabase.DefaultApiKeyProviderName;

    private sealed record RefreshDetectedDefaultsResult(bool Success, WslEnvironmentInfo? Info, string ErrorMessage);
}
