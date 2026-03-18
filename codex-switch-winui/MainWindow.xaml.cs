// 主窗口：展示组合列表与详情，并提供新增/编辑/删除/切换等入口。
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using codex_switch_winui.Dialogs;
using codex_switch_winui.Models;
using codex_switch_winui.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel;
using Windows.Graphics;

namespace codex_switch_winui;

public sealed partial class MainWindow : Window, INotifyPropertyChanged
{
    private const uint MessageBoxOk = 0x00000000;
    private const uint MessageBoxOkCancel = 0x00000001;
    private const uint MessageBoxIconError = 0x00000010;
    private const uint MessageBoxIconInformation = 0x00000040;
    private const int MessageBoxResultOk = 1;
    private const string DefaultConnectionTestModel = "gpt-5.4-mini";

    private readonly ProfileStore _store = new();
    private readonly CodexConfigService _codex = new();
    private readonly ProviderConnectionTestService _providerConnectionTest = new();

    private ProfileDatabase _database = new();
    private bool _isRefreshingDefaultWslEnvironment;
    private bool _suppressTargetSettingsUpdate;
    private bool _suppressSessionMigrationDaysUpdate;
    private XamlRoot? _xamlRoot;
    private IntPtr _windowHandle;
    private string? _startupErrorMessage;
    private bool _isSwitching;
    private bool _isTestingConnection;

    public ObservableCollection<CodexProfile> Profiles { get; } = new();

    private CodexProfile? _selectedProfile;
    public CodexProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (ReferenceEquals(_selectedProfile, value))
            {
                return;
            }

            _selectedProfile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedProfileName));
            OnPropertyChanged(nameof(SelectedProfileBaseUrl));
            OnPropertyChanged(nameof(SelectedProfileAuthModeText));
            OnPropertyChanged(nameof(SelectedProfileProviderCategoryText));
            OnPropertyChanged(nameof(SelectedProfileTestModelText));
            OnPropertyChanged(nameof(CanSwitchSelectedProfile));
            OnPropertyChanged(nameof(CanTestSelectedProfile));
            OnPropertyChanged(nameof(TestConnectionButtonVisibility));
        }
    }

    public string SelectedProfileName => SelectedProfile?.Name ?? "未选择配置";
    public string SelectedProfileBaseUrl =>
        SelectedProfile is null
            ? string.Empty
            : SelectedProfile.ProviderCategory == ProviderCategory.OpenAI
                ? "（无需配置，使用模板）"
                : !string.IsNullOrWhiteSpace(SelectedProfile.BaseUrl)
                    ? SelectedProfile.BaseUrl
                    : !string.IsNullOrWhiteSpace(SelectedProfile.StoredConfigTomlPath)
                        ? "（从已保存的 config.toml 读取）"
                        : string.Empty;
    public string SelectedProfileAuthModeText =>
        SelectedProfile is null
            ? string.Empty
            : SelectedProfile.AuthMode == CodexAuthMode.ApiKey
                ? "API Key"
                : "auth.json 文件";
    public string SelectedProfileProviderCategoryText =>
        SelectedProfile is null
            ? string.Empty
            : SelectedProfile.ProviderCategory == ProviderCategory.OpenAI
                ? "OpenAI"
                : "APIKEY";
    public string SelectedProfileTestModelText =>
        SelectedProfile is null
            ? string.Empty
            : SelectedProfile.ProviderCategory == ProviderCategory.OpenAI
                ? "（未启用）"
                : string.IsNullOrWhiteSpace(SelectedProfile.TestModel)
                    ? $"{DefaultConnectionTestModel}（默认）"
                    : SelectedProfile.TestModel!;
    public bool IsSwitching
    {
        get => _isSwitching;
        private set
        {
            if (_isSwitching == value)
            {
                return;
            }

            _isSwitching = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSwitchSelectedProfile));
            OnPropertyChanged(nameof(CanTestSelectedProfile));
            OnPropertyChanged(nameof(SwitchIconOpacity));
        }
    }

    public bool IsTestingConnection
    {
        get => _isTestingConnection;
        private set
        {
            if (_isTestingConnection == value)
            {
                return;
            }

            _isTestingConnection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSwitchSelectedProfile));
            OnPropertyChanged(nameof(CanTestSelectedProfile));
        }
    }

    public bool CanSwitchSelectedProfile => SelectedProfile is not null && !IsSwitching && !IsTestingConnection;
    public bool CanTestSelectedProfile =>
        SelectedProfile is { ProviderCategory: ProviderCategory.ApiKey } && !IsSwitching && !IsTestingConnection;
    public Visibility TestConnectionButtonVisibility =>
        SelectedProfile?.ProviderCategory == ProviderCategory.ApiKey ? Visibility.Visible : Visibility.Collapsed;

    public double SwitchIconOpacity => IsSwitching ? 0d : 1d;

    public event PropertyChangedEventHandler? PropertyChanged;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    public MainWindow()
    {
        InitializeComponent();
        TrySetWindowSizeAndCenter(1480, 1060);

        Activated += async (_, _) =>
        {
            _xamlRoot ??= (Content as FrameworkElement)?.XamlRoot;
            _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            TrySetWindowIcon();

            if (!string.IsNullOrWhiteSpace(_startupErrorMessage))
            {
                var message = _startupErrorMessage;
                _startupErrorMessage = null;
                await ShowErrorAsync("加载失败", message);
            }
        };

        LoadProfiles();
    }

    private void LoadProfiles()
    {
        try
        {
            _database = _store.Load();
            NormalizeApiKeyProviderName();
            NormalizeAndShowTargetSettings();
            NormalizeAndShowSessionMigrationDays();
            ScheduleDetectedWslEnvironmentRefresh();

            var sorted = _database.Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
            Profiles.Clear();
            foreach (var profile in sorted)
            {
                Profiles.Add(profile);
            }

            SelectedProfile = null;
            if (_database.LastSelectedProfileId is Guid lastId)
            {
                SelectedProfile = Profiles.FirstOrDefault(p => p.Id == lastId);
            }
        }
        catch (Exception ex)
        {
            _startupErrorMessage = FormatExceptionMessage(ex);
        }
    }

    private void NormalizeAndShowTargetSettings()
    {
        if (!_database.ReplaceWindowsTarget && !_database.ReplaceWslTarget)
        {
            _database.ReplaceWindowsTarget = true;
            _store.Save(_database);
        }

        if (ReplaceWindowsTargetCheckBox is not null && ReplaceWslTargetCheckBox is not null)
        {
            _suppressTargetSettingsUpdate = true;
            ReplaceWindowsTargetCheckBox.IsChecked = _database.ReplaceWindowsTarget;
            ReplaceWslTargetCheckBox.IsChecked = _database.ReplaceWslTarget;
            _suppressTargetSettingsUpdate = false;
        }

        UpdateWslTargetStatusText();
    }

    private void NormalizeAndShowSessionMigrationDays()
    {
        var days = _database.SessionMigrationDays;
        if (days < 0)
        {
            days = 3;
            _database.SessionMigrationDays = days;
            _store.Save(_database);
        }

        if (SessionMigrationDaysBox is null)
        {
            return;
        }

        _suppressSessionMigrationDaysUpdate = true;
        SessionMigrationDaysBox.Value = Math.Clamp(days, 0, 30);
        _suppressSessionMigrationDaysUpdate = false;
    }

    private void NormalizeApiKeyProviderName()
    {
        var providerName = NormalizeApiKeyProviderName(_database.ApiKeyProviderName);
        if (string.Equals(_database.ApiKeyProviderName, providerName, StringComparison.Ordinal))
        {
            return;
        }

        _database.ApiKeyProviderName = providerName;
        _store.Save(_database);
    }

    private void UpdateWslTargetStatusText()
    {
        if (WslTargetStatusTextBlock is null)
        {
            return;
        }

        if (!_database.ReplaceWslTarget)
        {
            WslTargetStatusTextBlock.Text = "WSL 未启用。启用后会自动使用默认发行版和默认用户。";
            return;
        }

        if (TryResolveWslEnvironmentFromStoredValues(out var info, out var sourceText, out var errorMessage) && info is not null)
        {
            var codexPath = $"{info.HomeDirectory.TrimEnd('/')}/.codex";
            WslTargetStatusTextBlock.Text = $"WSL 目标：{info.DistroName} / {info.UserName} -> {codexPath}（{sourceText}）";
            return;
        }

        WslTargetStatusTextBlock.Text = errorMessage;
    }

    private bool HasCustomWslSettings() =>
        !string.IsNullOrWhiteSpace(_database.WslDistroName)
        || !string.IsNullOrWhiteSpace(_database.WslUserName);

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new AddProfileDialog(GetXamlRoot(sender), GetWindowHandle());
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            if (dialog.ProviderCategory == ProviderCategory.OpenAI)
            {
                _store.AddProfileFromAuthJson(
                    _database,
                    dialog.ProfileName!,
                    dialog.BaseUrl ?? string.Empty,
                    dialog.AuthJsonPath!,
                    dialog.ProviderCategory,
                    configTomlSourcePath: null,
                    testModel: null);
            }
            else
            {
                if (dialog.AuthMode == CodexAuthMode.ApiKey)
                {
                    _store.AddProfileFromApiKey(
                        _database,
                        dialog.ProfileName!,
                        dialog.BaseUrl ?? string.Empty,
                        dialog.ApiKey!,
                        dialog.ProviderCategory,
                        dialog.ConfigTomlPath,
                        dialog.TestModel);
                }
                else
                {
                    _store.AddProfileFromAuthJson(
                        _database,
                        dialog.ProfileName!,
                        dialog.BaseUrl ?? string.Empty,
                        dialog.AuthJsonPath!,
                        dialog.ProviderCategory,
                        dialog.ConfigTomlPath,
                        dialog.TestModel);
                }
            }

            LoadProfiles();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("添加失败", FormatExceptionMessage(ex), sender);
        }
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        var profile = SelectedProfile;
        if (profile is null)
        {
            await ShowInfoAsync("提示", "请先选择一个组合。", sender);
            return;
        }

        try
        {
            var dialog = new AddProfileDialog(GetXamlRoot(sender), GetWindowHandle(), profile);
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            _store.UpdateProfile(
                _database,
                profile.Id,
                dialog.ProfileName!,
                dialog.ProviderCategory,
                dialog.ImportConfigToml,
                dialog.BaseUrl,
                dialog.ConfigTomlPath,
                dialog.AuthMode,
                dialog.AuthJsonPath,
                dialog.ApiKey,
                dialog.TestModel);

            _database.LastSelectedProfileId = profile.Id;
            _store.Save(_database);

            LoadProfiles();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("编辑失败", FormatExceptionMessage(ex), sender);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var profile = SelectedProfile;
        if (profile is null)
        {
            await ShowInfoAsync("提示", "请先选择一个组合。", sender);
            return;
        }

        var confirm = await ShowConfirmAsync("确认删除", $"确定删除：{profile.Name}？", primaryText: "删除", sender: sender);
        if (!confirm)
        {
            return;
        }

        try
        {
            _store.DeleteProfile(_database, profile.Id);
            LoadProfiles();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("删除失败", FormatExceptionMessage(ex), sender);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadProfiles();

    private void ReplaceTargetCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressTargetSettingsUpdate)
        {
            return;
        }

        _database.ReplaceWindowsTarget = ReplaceWindowsTargetCheckBox.IsChecked == true;
        _database.ReplaceWslTarget = ReplaceWslTargetCheckBox.IsChecked == true;

        if (!_database.ReplaceWindowsTarget && !_database.ReplaceWslTarget)
        {
            _database.ReplaceWindowsTarget = true;

            _suppressTargetSettingsUpdate = true;
            ReplaceWindowsTargetCheckBox.IsChecked = true;
            _suppressTargetSettingsUpdate = false;
        }

        _store.Save(_database);
        UpdateWslTargetStatusText();
        ScheduleDetectedWslEnvironmentRefresh();
    }

    private async void Switch_Click(object sender, RoutedEventArgs e)
    {
        if (IsSwitching)
        {
            return;
        }

        var profile = SelectedProfile;
        if (profile is null)
        {
            await ShowInfoAsync("提示", "请先选择一个组合。", sender);
            return;
        }

        IsSwitching = true;

        string? successMessage = null;
        string? errorMessage = null;

        try
        {
            var appliedTargets = await Task.Run(() => _codex.ApplyProfile(profile, _database));
            _database.LastSelectedProfileId = profile.Id;
            _store.Save(_database);
            successMessage = $"切换完成：{string.Join("、", appliedTargets)}。";
        }
        catch (Exception ex)
        {
            errorMessage = FormatExceptionMessage(ex);
        }
        finally
        {
            IsSwitching = false;
        }

        if (!string.IsNullOrEmpty(errorMessage))
        {
            await ShowErrorAsync("切换失败", errorMessage, sender);
            return;
        }

        await ShowInfoAsync("成功", successMessage ?? "切换完成。", sender);
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        if (IsTestingConnection)
        {
            return;
        }

        var profile = SelectedProfile;
        if (profile is null)
        {
            await ShowInfoAsync("提示", "请先选择一个组合。", sender);
            return;
        }

        if (profile.ProviderCategory != ProviderCategory.ApiKey)
        {
            await ShowInfoAsync("提示", "当前只支持测试 APIKEY 组合。", sender);
            return;
        }

        ConnectionTestContext context;
        Uri endpoint;

        try
        {
            context = ResolveConnectionTestContext(profile);
            endpoint = _providerConnectionTest.BuildResponsesEndpoint(context.BaseUrl);
        }
        catch (InvalidOperationException ex)
        {
            await ShowErrorAsync("测试失败", ex.Message, sender);
            return;
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("测试失败", FormatExceptionMessage(ex), sender);
            return;
        }

        IsTestingConnection = true;

        ProviderConnectionTestResult result;
        try
        {
            result = await _providerConnectionTest.TestResponsesAsync(context.BaseUrl, context.ApiKey, context.Model);
        }
        finally
        {
            IsTestingConnection = false;
        }

        var message = BuildConnectionTestDialogMessage(profile.Name, endpoint.AbsoluteUri, context.Model, result.Message);
        switch (result.Status)
        {
            case ProviderConnectionTestStatus.Success:
                await ShowInfoAsync("测试成功", message, sender);
                break;
            case ProviderConnectionTestStatus.Warning:
                await ShowInfoAsync("测试警告", message, sender);
                break;
            default:
                await ShowErrorAsync("测试失败", message, sender);
                break;
        }
    }

    private void SessionMigrationDaysBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressSessionMigrationDaysUpdate)
        {
            return;
        }

        if (double.IsNaN(sender.Value) || double.IsInfinity(sender.Value))
        {
            return;
        }

        var days = (int)Math.Round(sender.Value);
        days = Math.Clamp(days, 0, 30);

        if (days == _database.SessionMigrationDays)
        {
            return;
        }

        _database.SessionMigrationDays = days;
        _store.Save(_database);

        if (SessionMigrationDaysBox.Value != days)
        {
            _suppressSessionMigrationDaysUpdate = true;
            SessionMigrationDaysBox.Value = days;
            _suppressSessionMigrationDaysUpdate = false;
        }
    }

    private async void OpenCodexFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var targetDirectories = _codex.GetSelectedCodexDirectories(_database);
            foreach (var target in targetDirectories)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{target.Path}\"",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("打开失败", FormatExceptionMessage(ex), sender);
        }
    }

    private async void OpenBackupsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _codex.BackupsDirectoryPath;
            System.IO.Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("打开失败", FormatExceptionMessage(ex), sender);
        }
    }

    private async void EditTemplates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var window = new TemplateEditorWindow();
            window.Activate();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("打开失败", FormatExceptionMessage(ex), sender);
        }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _codex.TryGetCachedDefaultWslEnvironment(_database, out var cachedDefaultEnvironment);
            var dialog = new SettingsDialog(
                GetXamlRoot(sender),
                _database.ApiKeyProviderName,
                _database.WslDistroName,
                _database.WslUserName,
                cachedDefaultEnvironment,
                _database.CachedDefaultWslErrorMessage);

            var result = await dialog.ShowAsync();
            PersistDetectedWslEnvironmentRefresh(
                dialog.RefreshedDetectedEnvironment,
                dialog.RefreshedDetectedEnvironmentErrorMessage);

            if (result != ContentDialogResult.Primary)
            {
                UpdateWslTargetStatusText();
                return;
            }

            _database.ApiKeyProviderName = NormalizeApiKeyProviderName(dialog.ApiKeyProviderName);
            _database.WslDistroName = dialog.WslDistroName;
            _database.WslUserName = dialog.WslUserName;
            _store.Save(_database);
            UpdateWslTargetStatusText();
            ScheduleDetectedWslEnvironmentRefresh();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("打开失败", FormatExceptionMessage(ex), sender);
        }
    }

    private async void RestoreLatestBackup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var restoredTargets = _codex.RestoreLatestBackup(_database);
            if (restoredTargets.Count == 0)
            {
                await ShowInfoAsync("提示", "没有找到可恢复的备份。", sender);
                return;
            }

            await ShowInfoAsync("成功", $"已恢复：{string.Join("、", restoredTargets)}。", sender);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("恢复失败", FormatExceptionMessage(ex), sender);
        }
    }

    private void ProfilesList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => Switch_Click(sender, e);

    private async Task ShowInfoAsync(string title, string message, object? sender = null)
    {
        var xamlRoot = TryGetXamlRoot(sender);
        if (xamlRoot is null)
        {
            ShowNativeMessageBox(title, message, MessageBoxOk | MessageBoxIconInformation);
            return;
        }

        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = title,
                Content = message,
                CloseButtonText = "确定",
                DefaultButton = ContentDialogButton.Close
            };
            await dialog.ShowAsync();
        }
        catch
        {
            ShowNativeMessageBox(title, message, MessageBoxOk | MessageBoxIconInformation);
        }
    }

    private async Task ShowErrorAsync(string title, string message, object? sender = null)
    {
        var xamlRoot = TryGetXamlRoot(sender);
        if (xamlRoot is null)
        {
            ShowNativeMessageBox(title, message, MessageBoxOk | MessageBoxIconError);
            return;
        }

        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = title,
                Content = message,
                CloseButtonText = "确定",
                DefaultButton = ContentDialogButton.Close
            };
            await dialog.ShowAsync();
        }
        catch
        {
            ShowNativeMessageBox(title, message, MessageBoxOk | MessageBoxIconError);
        }
    }

    private static string FormatExceptionMessage(Exception ex)
    {
        var message = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
        var details = $"{ex.GetType().Name} (0x{ex.HResult:X8})";
        var lineNumber = TryGetExceptionProperty(ex, "LineNumber");
        var linePosition = TryGetExceptionProperty(ex, "LinePosition");
        var baseUri = TryGetExceptionProperty(ex, "BaseUri");

        if (!string.IsNullOrWhiteSpace(lineNumber) && lineNumber != "0")
        {
            details += string.IsNullOrWhiteSpace(linePosition) || linePosition == "0"
                ? $"\n行号：{lineNumber}"
                : $"\n位置：第 {lineNumber} 行，第 {linePosition} 列";
        }

        if (!string.IsNullOrWhiteSpace(baseUri))
        {
            details += $"\n资源：{baseUri}";
        }

        if (ex.InnerException is null)
        {
            return ShouldAppendFullExceptionText(ex)
                ? $"{message}\n\n{details}\n\n详细信息：\n{ex}"
                : $"{message}\n\n{details}";
        }

        var fullMessage = $"{message}\n\n{details}\n内部异常：{ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
        return ShouldAppendFullExceptionText(ex)
            ? $"{fullMessage}\n\n详细信息：\n{ex}"
            : fullMessage;
    }

    private async Task<bool> ShowConfirmAsync(string title, string message, string primaryText, object? sender = null)
    {
        var xamlRoot = TryGetXamlRoot(sender);
        if (xamlRoot is null)
        {
            return ShowNativeMessageBox(title, message, MessageBoxOkCancel | MessageBoxIconInformation) == MessageBoxResultOk;
        }

        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = title,
                Content = message,
                PrimaryButtonText = primaryText,
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }
        catch
        {
            return ShowNativeMessageBox(title, message, MessageBoxOkCancel | MessageBoxIconInformation) == MessageBoxResultOk;
        }
    }

    private static string? TryGetExceptionProperty(Exception ex, string propertyName)
    {
        try
        {
            var property = ex.GetType().GetProperty(propertyName);
            var value = property?.GetValue(ex);
            return value?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private int ShowNativeMessageBox(string title, string message, uint type)
    {
        try
        {
            return MessageBoxW(GetWindowHandle(), message, title, type);
        }
        catch
        {
            return 0;
        }
    }

    private static bool ShouldAppendFullExceptionText(Exception ex) =>
        ex.GetType().Name.Contains("XamlParseException", StringComparison.Ordinal);

    private XamlRoot GetXamlRoot(object? sender = null) =>
        TryGetXamlRoot(sender) ?? throw new InvalidOperationException("XamlRoot 尚未就绪，请先等待窗口显示后再试。");

    private XamlRoot? TryGetXamlRoot(object? sender = null)
    {
        if (sender is FrameworkElement element && element.XamlRoot is not null)
        {
            return element.XamlRoot;
        }

        if (_xamlRoot is not null)
        {
            return _xamlRoot;
        }

        return (Content as FrameworkElement)?.XamlRoot;
    }

    private IntPtr GetWindowHandle()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            return _windowHandle;
        }

        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        return _windowHandle;
    }

    private void TrySetWindowSizeAndCenter(int width, int height)
    {
        try
        {
            var hwnd = GetWindowHandle();
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            appWindow.Resize(new SizeInt32(width, height));
            WindowMinSize.SetMinSize(hwnd, width, height);

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

    private void TrySetWindowIcon()
    {
        try
        {
            var hwnd = GetWindowHandle();
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            var basePath = GetAppBasePath();
            var assetsDir = Path.Combine(basePath, "Assets");

            var preferred = new[]
            {
                Path.Combine(assetsDir, "app.ico"),
                Path.Combine(assetsDir, "1.ico")
            };

            var iconPath =
                preferred.FirstOrDefault(File.Exists)
                ?? (Directory.Exists(assetsDir)
                    ? Directory.EnumerateFiles(assetsDir, "*.ico").FirstOrDefault()
                    : null);

            if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static string GetAppBasePath()
    {
        try
        {
            return Package.Current.InstalledLocation.Path;
        }
        catch
        {
            return AppContext.BaseDirectory;
        }
    }

    private void ScheduleDetectedWslEnvironmentRefresh()
    {
        if (HasCustomWslSettings() || _isRefreshingDefaultWslEnvironment)
        {
            return;
        }

        _ = RefreshDetectedWslEnvironmentAsync();
    }

    private async Task RefreshDetectedWslEnvironmentAsync()
    {
        if (_isRefreshingDefaultWslEnvironment)
        {
            return;
        }

        _isRefreshingDefaultWslEnvironment = true;
        UpdateWslTargetStatusText();

        var databaseSnapshot = _database;

        try
        {
            var result = await Task.Run(() =>
            {
                var success = _codex.TryRefreshDefaultWslEnvironment(out var info, out var errorMessage);
                return new WslDefaultRefreshResult(success, info, errorMessage);
            });

            if (!ReferenceEquals(databaseSnapshot, _database))
            {
                return;
            }

            PersistDetectedWslEnvironmentRefresh(result.Info, result.ErrorMessage);
        }
        finally
        {
            _isRefreshingDefaultWslEnvironment = false;
            UpdateWslTargetStatusText();
        }
    }

    private void PersistDetectedWslEnvironmentRefresh(WslEnvironmentInfo? info, string? errorMessage)
    {
        var changed = false;

        if (info is not null)
        {
            changed |= !string.Equals(_database.CachedDefaultWslDistroName, info.DistroName, StringComparison.Ordinal);
            changed |= !string.Equals(_database.CachedDefaultWslUserName, info.UserName, StringComparison.Ordinal);
            changed |= !string.Equals(_database.CachedDefaultWslHomeDirectory, info.HomeDirectory, StringComparison.Ordinal);
            changed |= _database.CachedDefaultWslDetectedAtUtc is null;
            changed |= _database.CachedDefaultWslErrorMessage is not null;
            changed |= _database.CachedDefaultWslErrorAtUtc is not null;

            _database.CachedDefaultWslDistroName = info.DistroName;
            _database.CachedDefaultWslUserName = info.UserName;
            _database.CachedDefaultWslHomeDirectory = info.HomeDirectory;
            _database.CachedDefaultWslDetectedAtUtc = DateTime.UtcNow;
            _database.CachedDefaultWslErrorMessage = null;
            _database.CachedDefaultWslErrorAtUtc = null;
        }
        else if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            var normalizedErrorMessage = errorMessage.Trim();
            changed |= !string.Equals(_database.CachedDefaultWslErrorMessage, normalizedErrorMessage, StringComparison.Ordinal);
            changed |= _database.CachedDefaultWslErrorAtUtc is null;

            _database.CachedDefaultWslErrorMessage = normalizedErrorMessage;
            _database.CachedDefaultWslErrorAtUtc = DateTime.UtcNow;
        }

        if (changed)
        {
            _store.Save(_database);
        }
    }

    private bool TryResolveWslEnvironmentFromStoredValues(out WslEnvironmentInfo? info, out string sourceText, out string errorMessage)
    {
        var configuredDistroName = NormalizeOptionalValue(_database.WslDistroName);
        var configuredUserName = NormalizeOptionalValue(_database.WslUserName);
        var hasCachedDefault = _codex.TryGetCachedDefaultWslEnvironment(_database, out var cachedDefaultInfo);

        if (configuredDistroName is not null && configuredUserName is not null)
        {
            info = new WslEnvironmentInfo(configuredDistroName, configuredUserName, $"/home/{configuredUserName}");
            sourceText = "已使用设置里的值";
            errorMessage = string.Empty;
            return true;
        }

        if (configuredDistroName is null && configuredUserName is null)
        {
            if (hasCachedDefault && cachedDefaultInfo is not null)
            {
                info = cachedDefaultInfo;
                sourceText = "本地缓存";
                errorMessage = string.Empty;
                return true;
            }

            info = null;
            sourceText = string.Empty;
            errorMessage = _isRefreshingDefaultWslEnvironment
                ? "WSL 默认值尚未缓存，正在后台读取。"
                : NormalizeOptionalValue(_database.CachedDefaultWslErrorMessage) ?? "WSL 默认值尚未缓存。";
            return false;
        }

        if (hasCachedDefault && cachedDefaultInfo is not null)
        {
            var distroName = configuredDistroName ?? cachedDefaultInfo.DistroName;
            var userName = configuredUserName ?? cachedDefaultInfo.UserName;
            var homeDirectory = configuredUserName is not null
                ? $"/home/{userName}"
                : cachedDefaultInfo.HomeDirectory;

            info = new WslEnvironmentInfo(distroName, userName, homeDirectory);
            sourceText = "已使用设置里的值";
            errorMessage = string.Empty;
            return true;
        }

        info = null;
        sourceText = string.Empty;
        errorMessage = _isRefreshingDefaultWslEnvironment
            ? "设置里只填了一部分，正在后台读取默认 WSL 信息。"
            : "设置里只填了一部分，且默认值尚未缓存。";
        return false;
    }

    private static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeApiKeyProviderName(string? value) =>
        ApiKeyProviderNameRules.NormalizeOrDefault(value);

    private ConnectionTestContext ResolveConnectionTestContext(CodexProfile profile)
    {
        var baseUrl = ResolveConnectionTestBaseUrl(profile);
        var apiKey = ResolveConnectionTestApiKey(profile);
        var model = string.IsNullOrWhiteSpace(profile.TestModel)
            ? DefaultConnectionTestModel
            : profile.TestModel.Trim();
        return new ConnectionTestContext(baseUrl, apiKey, model);
    }

    private string ResolveConnectionTestBaseUrl(CodexProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.StoredConfigTomlPath))
        {
            if (!_providerConnectionTest.TryReadBaseUrlFromConfigToml(profile.StoredConfigTomlPath, out var baseUrl, out var errorMessage))
            {
                throw new InvalidOperationException(errorMessage);
            }

            return baseUrl!;
        }

        if (string.IsNullOrWhiteSpace(profile.BaseUrl))
        {
            throw new InvalidOperationException("当前组合没有可用的 Base URL。");
        }

        return profile.BaseUrl.Trim();
    }

    private string ResolveConnectionTestApiKey(CodexProfile profile)
    {
        if (profile.AuthMode == CodexAuthMode.ApiKey)
        {
            if (string.IsNullOrWhiteSpace(profile.ProtectedApiKeyBase64))
            {
                throw new InvalidOperationException("当前组合没有保存 API Key。");
            }

            try
            {
                var apiKey = ApiKeyProtection.UnprotectFromBase64(profile.ProtectedApiKeyBase64).Trim();
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    throw new InvalidOperationException("当前组合里的 API Key 为空。");
                }

                return apiKey;
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException)
            {
                throw new InvalidOperationException("当前组合里的 API Key 无法读取。", ex);
            }
        }

        if (!_providerConnectionTest.TryReadApiKeyFromAuthJson(profile.StoredAuthJsonPath ?? string.Empty, out var apiKeyFromFile, out var errorMessage))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return apiKeyFromFile!;
    }

    private static string BuildConnectionTestDialogMessage(string profileName, string endpoint, string model, string resultMessage) =>
        string.Join(
            Environment.NewLine,
            new[]
            {
                resultMessage,
                string.Empty,
                $"组合：{profileName}",
                $"请求地址：{endpoint}",
                $"测试模型：{model}"
            });

    private sealed record WslDefaultRefreshResult(bool Success, WslEnvironmentInfo? Info, string ErrorMessage);
    private sealed record ConnectionTestContext(string BaseUrl, string ApiKey, string Model);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
