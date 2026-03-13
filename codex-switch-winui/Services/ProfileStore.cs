// 组合存储：负责 profiles.json 的读写，以及每个组合相关文件（auth.json/config.toml）的持久化与更新。
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using codex_switch_winui.Models;

namespace codex_switch_winui.Services;

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string RootPath { get; }
    public string ProfilesPath { get; }
    public string DatabasePath { get; }

    public ProfileStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        RootPath = Path.Combine(appData, "codex-switch");
        ProfilesPath = Path.Combine(RootPath, "profiles");
        DatabasePath = Path.Combine(RootPath, "profiles.json");
    }

    public ProfileDatabase Load()
    {
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(ProfilesPath);

        if (!File.Exists(DatabasePath))
        {
            return new ProfileDatabase();
        }

        var json = File.ReadAllText(DatabasePath);
        return JsonSerializer.Deserialize<ProfileDatabase>(json, JsonOptions) ?? new ProfileDatabase();
    }

    public void Save(ProfileDatabase database)
    {
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(ProfilesPath);

        var json = JsonSerializer.Serialize(database, JsonOptions);
        File.WriteAllText(DatabasePath, json);
    }

    public CodexProfile AddProfileFromAuthJson(
        ProfileDatabase database,
        string name,
        string baseUrl,
        string authJsonSourcePath,
        ProviderCategory providerCategory,
        string? configTomlSourcePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(authJsonSourcePath);

        if (providerCategory != ProviderCategory.OpenAI && string.IsNullOrWhiteSpace(configTomlSourcePath))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        }

        if (!File.Exists(authJsonSourcePath))
        {
            throw new FileNotFoundException("auth.json 文件不存在。", authJsonSourcePath);
        }

        Directory.CreateDirectory(ProfilesPath);

        var id = Guid.NewGuid();
        var profileDir = GetProfileDirectoryPath(id);
        Directory.CreateDirectory(profileDir);

        var storedAuthPath = Path.Combine(profileDir, "auth.json");
        File.Copy(authJsonSourcePath, storedAuthPath, overwrite: true);

        string? storedConfigPath = null;
        if (providerCategory == ProviderCategory.OpenAI)
        {
            if (!string.IsNullOrWhiteSpace(configTomlSourcePath))
            {
                throw new InvalidOperationException("OpenAI 提供商不支持导入 config.toml。");
            }
        }
        else if (!string.IsNullOrWhiteSpace(configTomlSourcePath))
        {
            if (!File.Exists(configTomlSourcePath))
            {
                throw new FileNotFoundException("config.toml 文件不存在。", configTomlSourcePath);
            }

            storedConfigPath = Path.Combine(profileDir, "config.toml");
            File.Copy(configTomlSourcePath, storedConfigPath, overwrite: true);
        }

        var profile = new CodexProfile
        {
            Id = id,
            Name = name.Trim(),
            BaseUrl = baseUrl.Trim(),
            ProviderCategory = providerCategory,
            AuthMode = CodexAuthMode.AuthJsonFile,
            StoredAuthJsonPath = storedAuthPath,
            ProtectedApiKeyBase64 = null,
            StoredConfigTomlPath = storedConfigPath,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        database.Profiles.Add(profile);
        Save(database);
        return profile;
    }

    public CodexProfile AddProfileFromApiKey(
        ProfileDatabase database,
        string name,
        string baseUrl,
        string apiKey,
        ProviderCategory providerCategory,
        string? configTomlSourcePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        if (providerCategory == ProviderCategory.OpenAI)
        {
            throw new InvalidOperationException("OpenAI 提供商不支持 API Key 认证方式。");
        }

        if (string.IsNullOrWhiteSpace(configTomlSourcePath))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        }

        Directory.CreateDirectory(ProfilesPath);

        var id = Guid.NewGuid();
        var profileDir = GetProfileDirectoryPath(id);
        Directory.CreateDirectory(profileDir);

        string? storedConfigPath = null;
        if (!string.IsNullOrWhiteSpace(configTomlSourcePath))
        {
            if (!File.Exists(configTomlSourcePath))
            {
                throw new FileNotFoundException("config.toml 文件不存在。", configTomlSourcePath);
            }

            storedConfigPath = Path.Combine(profileDir, "config.toml");
            File.Copy(configTomlSourcePath, storedConfigPath, overwrite: true);
        }

        var profile = new CodexProfile
        {
            Id = id,
            Name = name.Trim(),
            BaseUrl = baseUrl.Trim(),
            ProviderCategory = providerCategory,
            AuthMode = CodexAuthMode.ApiKey,
            StoredAuthJsonPath = null,
            ProtectedApiKeyBase64 = ApiKeyProtection.ProtectToBase64(apiKey.Trim()),
            StoredConfigTomlPath = storedConfigPath,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        database.Profiles.Add(profile);
        Save(database);
        return profile;
    }

    public CodexProfile UpdateProfile(
        ProfileDatabase database,
        Guid profileId,
        string name,
        ProviderCategory providerCategory,
        bool importConfigToml,
        string? baseUrl,
        string? configTomlSourcePath,
        CodexAuthMode authMode,
        string? authJsonSourcePath,
        string? apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var profile = database.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile is null)
        {
            throw new InvalidOperationException("未找到要编辑的组合。");
        }

        Directory.CreateDirectory(ProfilesPath);

        var profileDir = GetProfileDirectoryPath(profileId);
        Directory.CreateDirectory(profileDir);

        profile.Name = name.Trim();
        profile.ProviderCategory = providerCategory;
        profile.UpdatedAtUtc = DateTime.UtcNow;

        if (providerCategory == ProviderCategory.OpenAI)
        {
            profile.AuthMode = CodexAuthMode.AuthJsonFile;

            if (!string.IsNullOrWhiteSpace(authJsonSourcePath))
            {
                if (!File.Exists(authJsonSourcePath))
                {
                    throw new FileNotFoundException("auth.json 文件不存在。", authJsonSourcePath);
                }

                var storedAuthPath = Path.Combine(profileDir, "auth.json");
                File.Copy(authJsonSourcePath, storedAuthPath, overwrite: true);
                profile.StoredAuthJsonPath = storedAuthPath;
            }

            if (string.IsNullOrWhiteSpace(profile.StoredAuthJsonPath) || !File.Exists(profile.StoredAuthJsonPath))
            {
                throw new InvalidOperationException("OpenAI 提供商需要 auth.json 文件。");
            }

            profile.BaseUrl = string.Empty;
            Save(database);
            return profile;
        }

        profile.BaseUrl = (baseUrl ?? string.Empty).Trim();

        if (importConfigToml)
        {
            if (!string.IsNullOrWhiteSpace(configTomlSourcePath))
            {
                if (!File.Exists(configTomlSourcePath))
                {
                    throw new FileNotFoundException("config.toml 文件不存在。", configTomlSourcePath);
                }

                var storedConfigPath = Path.Combine(profileDir, "config.toml");
                File.Copy(configTomlSourcePath, storedConfigPath, overwrite: true);
                profile.StoredConfigTomlPath = storedConfigPath;
            }
            else if (string.IsNullOrWhiteSpace(profile.StoredConfigTomlPath) || !File.Exists(profile.StoredConfigTomlPath))
            {
                throw new InvalidOperationException("请选择 config.toml。");
            }
        }
        else
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(profile.BaseUrl);
            profile.StoredConfigTomlPath = null;
        }

        profile.AuthMode = authMode;

        if (authMode == CodexAuthMode.ApiKey)
        {
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                profile.ProtectedApiKeyBase64 = ApiKeyProtection.ProtectToBase64(apiKey.Trim());
            }
            else if (string.IsNullOrWhiteSpace(profile.ProtectedApiKeyBase64))
            {
                throw new InvalidOperationException("请输入 API Key。");
            }

            profile.StoredAuthJsonPath = null;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(authJsonSourcePath))
            {
                if (!File.Exists(authJsonSourcePath))
                {
                    throw new FileNotFoundException("auth.json 文件不存在。", authJsonSourcePath);
                }

                var storedAuthPath = Path.Combine(profileDir, "auth.json");
                File.Copy(authJsonSourcePath, storedAuthPath, overwrite: true);
                profile.StoredAuthJsonPath = storedAuthPath;
            }
            else if (string.IsNullOrWhiteSpace(profile.StoredAuthJsonPath) || !File.Exists(profile.StoredAuthJsonPath))
            {
                throw new InvalidOperationException("请选择 auth.json。");
            }

            profile.ProtectedApiKeyBase64 = null;
        }

        Save(database);
        return profile;
    }

    public void DeleteProfile(ProfileDatabase database, Guid profileId)
    {
        var index = database.Profiles.FindIndex(p => p.Id == profileId);
        if (index < 0)
        {
            return;
        }

        database.Profiles.RemoveAt(index);
        if (database.LastSelectedProfileId == profileId)
        {
            database.LastSelectedProfileId = null;
        }

        Save(database);

        var profileDir = GetProfileDirectoryPath(profileId);
        if (Directory.Exists(profileDir))
        {
            Directory.Delete(profileDir, recursive: true);
        }
    }

    private string GetProfileDirectoryPath(Guid profileId) => Path.Combine(ProfilesPath, profileId.ToString("N"));
}
