// 新建/编辑组合对话框：收集提供商分类、配置方式与认证方式等输入，并在确认时输出结构化结果给主窗口。
using System;
using System.IO;
using codex_switch_winui.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace codex_switch_winui.Dialogs;

public sealed partial class AddProfileDialog : ContentDialog
{
    private readonly IntPtr _ownerHwnd;
    private readonly CodexProfile? _editingProfile;
    private bool _initializing;

    public Guid? EditingProfileId => _editingProfile?.Id;
    public bool IsEditMode => _editingProfile is not null;

    public string? ProfileName { get; private set; }
    public string? BaseUrl { get; private set; }
    public string? ConfigTomlPath { get; private set; }
    public bool ImportConfigToml { get; private set; }

    public ProviderCategory ProviderCategory { get; private set; } = ProviderCategory.ApiKey;

    public CodexAuthMode AuthMode { get; private set; } = CodexAuthMode.AuthJsonFile;
    public string? AuthJsonPath { get; private set; }
    public string? ApiKey { get; private set; }
    public string? TestModel { get; private set; }

    public AddProfileDialog(XamlRoot xamlRoot, IntPtr ownerHwnd, CodexProfile? editingProfile = null)
    {
        _ownerHwnd = ownerHwnd;
        _editingProfile = editingProfile;

        InitializeComponent();

        XamlRoot = xamlRoot;
        Title = editingProfile is null ? "添加组合" : "编辑组合";
        PrimaryButtonText = editingProfile is null ? "确定" : "保存";
        CloseButtonText = "取消";
        DefaultButton = ContentDialogButton.Primary;

        PrimaryButtonClick += OnPrimaryButtonClick;
        Loaded += (_, _) =>
        {
            InitializeFromEditingProfile();
            UpdateProviderCategoryUi();
        };
    }

    private void ConfigMode_Checked(object sender, RoutedEventArgs e) => UpdateConfigModeUi();

    private void UpdateConfigModeUi()
    {
        var importConfig = ImportConfigModeRadio?.IsChecked == true;
        ImportConfigToml = importConfig;

        if (ConfigTomlPanel is null || ConfigPathBox is null || BaseUrlPanel is null || BaseUrlBox is null)
        {
            return;
        }

        ConfigTomlPanel.Visibility = importConfig ? Visibility.Visible : Visibility.Collapsed;
        BaseUrlPanel.Visibility = importConfig ? Visibility.Collapsed : Visibility.Visible;

        if (_initializing)
        {
            return;
        }

        if (importConfig && !IsEditMode)
        {
            BaseUrlBox.Text = string.Empty;
            BaseUrl = string.Empty;
            return;
        }

        if (!importConfig)
        {
            ConfigPathBox.Text = string.Empty;
            ConfigTomlPath = null;
        }
    }

    private void AuthMode_Checked(object sender, RoutedEventArgs e) => UpdateAuthModeUi();

    private void UpdateAuthModeUi()
    {
        var useApiKey = ApiKeyRadio?.IsChecked == true;
        AuthMode = useApiKey ? CodexAuthMode.ApiKey : CodexAuthMode.AuthJsonFile;

        if (AuthJsonPanel is null || ApiKeyPanel is null || AuthPathBox is null || ApiKeyBox is null)
        {
            return;
        }

        AuthJsonPanel.Visibility = useApiKey ? Visibility.Collapsed : Visibility.Visible;
        ApiKeyPanel.Visibility = useApiKey ? Visibility.Visible : Visibility.Collapsed;

        if (_initializing)
        {
            return;
        }

        if (useApiKey && !IsEditMode)
        {
            AuthPathBox.Text = string.Empty;
            return;
        }

        if (!useApiKey)
        {
            ApiKeyBox.Password = string.Empty;
        }
    }

    private void ProviderCategory_Checked(object sender, RoutedEventArgs e) => UpdateProviderCategoryUi();

    private void UpdateProviderCategoryUi()
    {
        var isOpenAi = ProviderOpenAiRadio?.IsChecked == true;
        ProviderCategory = isOpenAi ? ProviderCategory.OpenAI : ProviderCategory.ApiKey;

        if (ConfigSection is not null)
        {
            ConfigSection.Visibility = isOpenAi ? Visibility.Collapsed : Visibility.Visible;
        }

        if (AuthModeRadioButtons is not null)
        {
            AuthModeRadioButtons.Visibility = isOpenAi ? Visibility.Collapsed : Visibility.Visible;
        }

        if (TestModelSection is not null)
        {
            TestModelSection.Visibility = isOpenAi ? Visibility.Collapsed : Visibility.Visible;
        }

        if (isOpenAi)
        {
            AuthMode = CodexAuthMode.AuthJsonFile;

            if (AuthJsonRadio is not null)
            {
                AuthJsonRadio.IsChecked = true;
            }

            if (AuthJsonPanel is not null)
            {
                AuthJsonPanel.Visibility = Visibility.Visible;
            }

            if (ApiKeyPanel is not null)
            {
                ApiKeyPanel.Visibility = Visibility.Collapsed;
            }

            if (_initializing)
            {
                return;
            }

            if (BaseUrlBox is not null)
            {
                BaseUrlBox.Text = string.Empty;
            }

            if (ConfigPathBox is not null)
            {
                ConfigPathBox.Text = string.Empty;
            }

            if (ApiKeyBox is not null)
            {
                ApiKeyBox.Password = string.Empty;
            }

            BaseUrl = string.Empty;
            ConfigTomlPath = null;
            ImportConfigToml = false;
            TestModel = null;

            if (TestModelBox is not null)
            {
                TestModelBox.Text = string.Empty;
            }
        }
        else
        {
            UpdateConfigModeUi();
            UpdateAuthModeUi();
        }
    }

    private void InitializeFromEditingProfile()
    {
        if (_editingProfile is null)
        {
            return;
        }

        _initializing = true;

        if (NameBox is not null)
        {
            NameBox.Text = _editingProfile.Name;
        }

        if (BaseUrlBox is not null)
        {
            BaseUrlBox.Text = _editingProfile.BaseUrl;
            BaseUrlBox.PlaceholderText = "留空保持不变";
        }

        if (ConfigPathBox is not null)
        {
            ConfigPathBox.PlaceholderText = "留空保持不变（可重新选择 config.toml）";
        }

        if (AuthPathBox is not null)
        {
            AuthPathBox.PlaceholderText = "留空保持不变（可重新选择 auth.json）";
        }

        if (ApiKeyBox is not null)
        {
            ApiKeyBox.PlaceholderText = "留空保持不变";
        }

        if (TestModelBox is not null)
        {
            TestModelBox.Text = _editingProfile.TestModel ?? string.Empty;
        }

        var isOpenAi = _editingProfile.ProviderCategory == ProviderCategory.OpenAI;
        if (ProviderOpenAiRadio is not null)
        {
            ProviderOpenAiRadio.IsChecked = isOpenAi;
        }

        if (ProviderApiKeyRadio is not null)
        {
            ProviderApiKeyRadio.IsChecked = !isOpenAi;
        }

        if (!isOpenAi)
        {
            var importConfig = !string.IsNullOrWhiteSpace(_editingProfile.StoredConfigTomlPath);
            if (ImportConfigModeRadio is not null)
            {
                ImportConfigModeRadio.IsChecked = importConfig;
            }

            if (BaseUrlModeRadio is not null)
            {
                BaseUrlModeRadio.IsChecked = !importConfig;
            }

            if (ApiKeyRadio is not null)
            {
                ApiKeyRadio.IsChecked = _editingProfile.AuthMode == CodexAuthMode.ApiKey;
            }

            if (AuthJsonRadio is not null)
            {
                AuthJsonRadio.IsChecked = _editingProfile.AuthMode != CodexAuthMode.ApiKey;
            }
        }
        else
        {
            if (AuthJsonRadio is not null)
            {
                AuthJsonRadio.IsChecked = true;
            }
        }

        _initializing = false;
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _ownerHwnd);

            picker.FileTypeFilter.Add(".json");
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            AuthPathBox.Text = file.Path;
        }
        catch (Exception ex)
        {
            ShowValidationError(ex.Message);
        }
    }

    private async void BrowseConfigToml_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _ownerHwnd);

            picker.FileTypeFilter.Add(".toml");
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            ConfigPathBox.Text = file.Path;
            ConfigTomlPath = file.Path;
            ErrorBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            ShowValidationError(ex.Message);
        }
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var name = NameBox.Text?.Trim();
        var baseUrl = BaseUrlBox.Text?.Trim();
        var configPath = ConfigPathBox.Text?.Trim();
        var authPath = AuthPathBox.Text?.Trim();
        var apiKey = ApiKeyBox.Password?.Trim();
        var testModel = TestModelBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            args.Cancel = true;
            ShowValidationError("请输入名称。");
            NameBox.Focus(FocusState.Programmatic);
            return;
        }

        UpdateProviderCategoryUi();

        if (ProviderCategory == ProviderCategory.OpenAI)
        {
            var needsAuthFile = _editingProfile is null
                || string.IsNullOrWhiteSpace(_editingProfile.StoredAuthJsonPath)
                || !File.Exists(_editingProfile.StoredAuthJsonPath);

            if (string.IsNullOrWhiteSpace(authPath) && needsAuthFile)
            {
                args.Cancel = true;
                ShowValidationError("请选择 auth.json。");
                return;
            }

            ProfileName = name;
            BaseUrl = string.Empty;
            ImportConfigToml = false;
            ConfigTomlPath = null;
            AuthMode = CodexAuthMode.AuthJsonFile;
            AuthJsonPath = string.IsNullOrWhiteSpace(authPath) ? null : authPath;
            ApiKey = null;
            TestModel = null;
            ErrorBar.IsOpen = false;
            return;
        }

        var importConfig = ImportConfigModeRadio?.IsChecked == true;
        ImportConfigToml = importConfig;

        if (importConfig)
        {
            var needsConfigFile = _editingProfile is null
                || string.IsNullOrWhiteSpace(_editingProfile.StoredConfigTomlPath)
                || !File.Exists(_editingProfile.StoredConfigTomlPath);

            if (string.IsNullOrWhiteSpace(configPath) && needsConfigFile)
            {
                args.Cancel = true;
                ShowValidationError("请选择 config.toml。");
                return;
            }
        }
        else
        {
            var effectiveBaseUrl = baseUrl;
            if (IsEditMode && string.IsNullOrWhiteSpace(effectiveBaseUrl))
            {
                effectiveBaseUrl = _editingProfile?.BaseUrl;
            }

            if (string.IsNullOrWhiteSpace(effectiveBaseUrl))
            {
                args.Cancel = true;
                ShowValidationError("请输入 Base URL。");
                BaseUrlBox.Focus(FocusState.Programmatic);
                return;
            }

            baseUrl = effectiveBaseUrl;
        }

        if (AuthMode == CodexAuthMode.ApiKey)
        {
            var needsApiKey = _editingProfile is null || string.IsNullOrWhiteSpace(_editingProfile.ProtectedApiKeyBase64);
            if (string.IsNullOrWhiteSpace(apiKey) && needsApiKey)
            {
                args.Cancel = true;
                ShowValidationError("请输入 API Key。");
                ApiKeyBox.Focus(FocusState.Programmatic);
                return;
            }

            ProfileName = name;
            BaseUrl = importConfig ? (_editingProfile?.BaseUrl ?? string.Empty) : baseUrl;
            ConfigTomlPath = string.IsNullOrWhiteSpace(configPath) ? null : configPath;
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
            AuthJsonPath = null;
            TestModel = string.IsNullOrWhiteSpace(testModel) ? null : testModel;
            ErrorBar.IsOpen = false;
            return;
        }

        var needsAuthJson = _editingProfile is null
            || string.IsNullOrWhiteSpace(_editingProfile.StoredAuthJsonPath)
            || !File.Exists(_editingProfile.StoredAuthJsonPath);

        if (string.IsNullOrWhiteSpace(authPath) && needsAuthJson)
        {
            args.Cancel = true;
            ShowValidationError("请选择 auth.json。");
            return;
        }

        ProfileName = name;
        BaseUrl = importConfig ? (_editingProfile?.BaseUrl ?? string.Empty) : baseUrl;
        ConfigTomlPath = string.IsNullOrWhiteSpace(configPath) ? null : configPath;
        AuthJsonPath = string.IsNullOrWhiteSpace(authPath) ? null : authPath;
        ApiKey = null;
        TestModel = string.IsNullOrWhiteSpace(testModel) ? null : testModel;
        ErrorBar.IsOpen = false;
    }

    private void ShowValidationError(string message)
    {
        if (ErrorBar is null)
        {
            return;
        }

        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}
