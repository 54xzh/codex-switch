// Codex 配置应用：将选定组合写入目标 ~/.codex/，并在写入前自动创建备份。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using codex_switch_winui.Models;

namespace codex_switch_winui.Services;

public sealed class CodexConfigService
{
    private static readonly Regex ModelProviderRegex = new(
        "(?<prefix>\"model_provider\"\\s*:\\s*\")(?<value>[^\"\\\\]*(?:\\\\.[^\"\\\\]*)*)(?<suffix>\")",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ConfigTemplateStore _templates = new();
    private readonly WslEnvironmentService _wsl = new();

    public string CodexDirectoryPath { get; }
    public string BackupsDirectoryPath { get; }

    public CodexConfigService()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        CodexDirectoryPath = Path.Combine(userProfile, ".codex");
        BackupsDirectoryPath = Path.Combine(userProfile, "codex-switch-backups");
    }

    public bool TryGetDefaultWslEnvironment(out WslEnvironmentInfo? info, out string errorMessage) =>
        _wsl.TryGetDefaultEnvironment(out info, out errorMessage);

    public bool TryRefreshDefaultWslEnvironment(out WslEnvironmentInfo? info, out string errorMessage) =>
        _wsl.TryRefreshDefaultEnvironment(out info, out errorMessage);

    public bool TryResolveWslEnvironment(ProfileDatabase database, out WslEnvironmentInfo? info, out string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(database);

        try
        {
            info = ResolveWslEnvironment(database);
            errorMessage = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            info = null;
            errorMessage = ex.Message;
            return false;
        }
    }

    public bool TryGetCachedDefaultWslEnvironment(ProfileDatabase database, out WslEnvironmentInfo? info)
    {
        ArgumentNullException.ThrowIfNull(database);

        var distroName = NormalizeOptionalValue(database.CachedDefaultWslDistroName);
        var userName = NormalizeOptionalValue(database.CachedDefaultWslUserName);
        var homeDirectory = NormalizeOptionalValue(database.CachedDefaultWslHomeDirectory);

        if (distroName is null || userName is null || homeDirectory is null)
        {
            info = null;
            return false;
        }

        info = new WslEnvironmentInfo(distroName, userName, homeDirectory);
        return true;
    }

    public IReadOnlyList<string> ApplyProfile(CodexProfile profile, ProfileDatabase database)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(database);

        var targets = ResolveSelectedTargets(database);
        EnsureTargetsExist(targets);

        var appliedTargets = new List<string>(targets.Count);
        foreach (var target in targets)
        {
            CreateBackupSetIfNeeded(target);
            ReplaceAuthJson(profile, target.CodexDirectoryPath);
            ReplaceConfigToml(profile, target.CodexDirectoryPath);
            TryMigrateRecentSessionModelProviders(target.CodexDirectoryPath, profile.ProviderCategory, database.SessionMigrationDays);
            appliedTargets.Add(target.DisplayName);
        }

        return appliedTargets;
    }

    public IReadOnlyList<string> RestoreLatestBackup(ProfileDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        var targets = ResolveSelectedTargets(database);
        var restoredTargets = new List<string>(targets.Count);

        foreach (var target in targets)
        {
            var latestDir = GetLatestBackupSetDirectory(target.BackupsDirectoryPath);
            if (latestDir is null)
            {
                continue;
            }

            Directory.CreateDirectory(target.CodexDirectoryPath);

            var restoredAny = false;

            var authBackup = Path.Combine(latestDir, "auth.json");
            if (File.Exists(authBackup))
            {
                File.Copy(authBackup, Path.Combine(target.CodexDirectoryPath, "auth.json"), overwrite: true);
                restoredAny = true;
            }

            var configBackup = Path.Combine(latestDir, "config.toml");
            if (File.Exists(configBackup))
            {
                File.Copy(configBackup, Path.Combine(target.CodexDirectoryPath, "config.toml"), overwrite: true);
                restoredAny = true;
            }

            if (restoredAny)
            {
                restoredTargets.Add(target.DisplayName);
            }
        }

        return restoredTargets;
    }

    public IReadOnlyList<CodexTargetDirectoryInfo> GetSelectedCodexDirectories(ProfileDatabase database) =>
        ResolveSelectedTargets(database)
            .Select(target => new CodexTargetDirectoryInfo(target.DisplayName, target.CodexDirectoryPath))
            .ToList();

    private List<CodexTargetContext> ResolveSelectedTargets(ProfileDatabase database)
    {
        var targets = new List<CodexTargetContext>(capacity: 2);

        if (database.ReplaceWindowsTarget)
        {
            targets.Add(new CodexTargetContext(
                DisplayName: "Windows",
                CodexDirectoryPath: CodexDirectoryPath,
                BackupsDirectoryPath: Path.Combine(BackupsDirectoryPath, "windows")));
        }

        if (database.ReplaceWslTarget)
        {
            var info = ResolveWslEnvironment(database);
            var codexLinuxPath = CombineLinuxPath(info.HomeDirectory, ".codex");
            var codexWindowsPath = _wsl.ToWindowsPath(info.DistroName, codexLinuxPath);
            var backupLeaf = $"{SanitizeDirectorySegment(info.DistroName)}-{SanitizeDirectorySegment(info.UserName)}";

            targets.Add(new CodexTargetContext(
                DisplayName: $"WSL ({info.DistroName}/{info.UserName})",
                CodexDirectoryPath: codexWindowsPath,
                BackupsDirectoryPath: Path.Combine(BackupsDirectoryPath, "wsl", backupLeaf)));
        }

        if (targets.Count == 0)
        {
            throw new InvalidOperationException("请至少选择一个替换目标。");
        }

        return targets;
    }

    private static void EnsureTargetsExist(IEnumerable<CodexTargetContext> targets)
    {
        foreach (var target in targets)
        {
            if (Directory.Exists(target.CodexDirectoryPath))
            {
                continue;
            }

            throw new DirectoryNotFoundException($"未找到 {target.DisplayName} 的 Codex 配置目录：{target.CodexDirectoryPath}");
        }
    }

    private void ReplaceAuthJson(CodexProfile profile, string codexDirectoryPath)
    {
        var destAuthPath = Path.Combine(codexDirectoryPath, "auth.json");

        if (profile.ProviderCategory == ProviderCategory.OpenAI)
        {
            if (profile.AuthMode != CodexAuthMode.AuthJsonFile)
            {
                throw new InvalidOperationException("OpenAI 提供商仅支持使用 auth.json 文件认证。");
            }

            if (string.IsNullOrWhiteSpace(profile.StoredAuthJsonPath))
            {
                throw new InvalidOperationException("此组合未保存 auth.json 文件路径。");
            }

            if (!File.Exists(profile.StoredAuthJsonPath))
            {
                throw new FileNotFoundException("已保存的 auth.json 不存在。", profile.StoredAuthJsonPath);
            }

            File.Copy(profile.StoredAuthJsonPath, destAuthPath, overwrite: true);
            return;
        }

        switch (profile.AuthMode)
        {
            case CodexAuthMode.AuthJsonFile:
            {
                if (string.IsNullOrWhiteSpace(profile.StoredAuthJsonPath))
                {
                    throw new InvalidOperationException("此组合未保存 auth.json 文件路径。");
                }

                if (!File.Exists(profile.StoredAuthJsonPath))
                {
                    throw new FileNotFoundException("已保存的 auth.json 不存在。", profile.StoredAuthJsonPath);
                }

                File.Copy(profile.StoredAuthJsonPath, destAuthPath, overwrite: true);
                return;
            }
            case CodexAuthMode.ApiKey:
            {
                if (string.IsNullOrWhiteSpace(profile.ProtectedApiKeyBase64))
                {
                    throw new InvalidOperationException("此组合未保存 API Key。");
                }

                var apiKey = ApiKeyProtection.UnprotectFromBase64(profile.ProtectedApiKeyBase64);
                var payload = new Dictionary<string, string> { ["OPENAI_API_KEY"] = apiKey };
                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(destAuthPath, json);
                return;
            }
            default:
                throw new InvalidOperationException($"未知认证方式：{profile.AuthMode}");
        }
    }

    private void ReplaceConfigToml(CodexProfile profile, string codexDirectoryPath)
    {
        var configPath = Path.Combine(codexDirectoryPath, "config.toml");

        if (profile.ProviderCategory == ProviderCategory.OpenAI)
        {
            var openAiTemplate = NormalizeLineEndings(_templates.LoadOpenAiTemplate());
            File.WriteAllText(
                configPath,
                EnsureTrailingNewLine(openAiTemplate),
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return;
        }

        if (!string.IsNullOrWhiteSpace(profile.StoredConfigTomlPath))
        {
            if (!File.Exists(profile.StoredConfigTomlPath))
            {
                throw new FileNotFoundException("已保存的 config.toml 不存在。", profile.StoredConfigTomlPath);
            }

            var storedConfig = NormalizeLineEndings(File.ReadAllText(profile.StoredConfigTomlPath));
            File.WriteAllText(
                configPath,
                EnsureTrailingNewLine(storedConfig),
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(profile.BaseUrl);
        var baseUrl = profile.BaseUrl.Trim();

        var apiKeyTemplate = NormalizeLineEndings(_templates.LoadApiKeyTemplate());
        var rendered = apiKeyTemplate.Replace("{base_url}", ToTomlString(baseUrl), StringComparison.Ordinal);
        File.WriteAllText(
            configPath,
            EnsureTrailingNewLine(rendered),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string EnsureTrailingNewLine(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return content;
        }

        return content.EndsWith("\n", StringComparison.Ordinal) ? content : content + "\n";
    }

    private static string NormalizeLineEndings(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return content;
        }

        return content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
    }

    private static string ToTomlString(string value)
    {
        var escaped = value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }

    private void TryMigrateRecentSessionModelProviders(string codexDirectoryPath, ProviderCategory providerCategory, int sessionMigrationDays)
    {
        try
        {
            if (sessionMigrationDays <= 0)
            {
                return;
            }

            var days = Math.Clamp(sessionMigrationDays, 1, 30);
            var sessionsRoot = Path.Combine(codexDirectoryPath, "sessions");
            if (!Directory.Exists(sessionsRoot))
            {
                return;
            }

            var targetProvider = providerCategory == ProviderCategory.OpenAI ? "openai" : "right";
            var today = DateTime.Today;

            for (var i = 0; i < days; i++)
            {
                var date = today.AddDays(-i);
                var dayDir = Path.Combine(
                    sessionsRoot,
                    date.ToString("yyyy", CultureInfo.InvariantCulture),
                    date.ToString("MM", CultureInfo.InvariantCulture),
                    date.ToString("dd", CultureInfo.InvariantCulture));

                if (!Directory.Exists(dayDir))
                {
                    continue;
                }

                foreach (var filePath in Directory.EnumerateFiles(dayDir, "*.jsonl", SearchOption.TopDirectoryOnly))
                {
                    TryRewriteSessionFirstLineModelProvider(filePath, targetProvider);
                }
            }
        }
        catch
        {
            // ignore
        }
    }

    private static void TryRewriteSessionFirstLineModelProvider(string filePath, string targetProvider)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var firstLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(firstLine))
            {
                return;
            }

            var match = ModelProviderRegex.Match(firstLine);
            if (!match.Success)
            {
                return;
            }

            var current = match.Groups["value"].Value;

            if (string.Equals(current, targetProvider, StringComparison.Ordinal))
            {
                return;
            }

            var newFirstLine = ModelProviderRegex.Replace(
                firstLine,
                m => $"{m.Groups["prefix"].Value}{targetProvider}{m.Groups["suffix"].Value}",
                count: 1,
                startat: 0);

            var tempPath = filePath + ".tmp";
            using (var writer = new StreamWriter(tempPath, append: false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.NewLine = "\n";
                writer.WriteLine(newFirstLine);

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (line is null)
                    {
                        break;
                    }

                    writer.WriteLine(line);
                }
            }

            File.Copy(tempPath, filePath, overwrite: true);
            File.Delete(tempPath);
        }
        catch
        {
            // ignore
        }
    }

    private void CreateBackupSetIfNeeded(CodexTargetContext target)
    {
        var authPath = Path.Combine(target.CodexDirectoryPath, "auth.json");
        var configPath = Path.Combine(target.CodexDirectoryPath, "config.toml");

        if (!File.Exists(authPath) && !File.Exists(configPath))
        {
            return;
        }

        Directory.CreateDirectory(target.BackupsDirectoryPath);

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        var backupDir = Path.Combine(target.BackupsDirectoryPath, stamp);
        Directory.CreateDirectory(backupDir);

        if (File.Exists(authPath))
        {
            File.Copy(authPath, Path.Combine(backupDir, "auth.json"), overwrite: true);
        }

        if (File.Exists(configPath))
        {
            File.Copy(configPath, Path.Combine(backupDir, "config.toml"), overwrite: true);
        }
    }

    private static string? GetLatestBackupSetDirectory(string backupsDirectoryPath)
    {
        if (!Directory.Exists(backupsDirectoryPath))
        {
            return null;
        }

        var dirs = Directory.GetDirectories(backupsDirectoryPath);
        if (dirs.Length == 0)
        {
            return null;
        }

        return dirs.OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    private static string CombineLinuxPath(string left, string right)
    {
        var normalizedLeft = left.Trim().TrimEnd('/').Replace('\\', '/');
        var normalizedRight = right.Trim().TrimStart('/').Replace('\\', '/');
        return $"{normalizedLeft}/{normalizedRight}";
    }

    private WslEnvironmentInfo ResolveWslEnvironment(ProfileDatabase database)
    {
        var configuredDistroName = NormalizeOptionalValue(database.WslDistroName);
        var configuredUserName = NormalizeOptionalValue(database.WslUserName);

        if (configuredDistroName is not null && configuredUserName is not null)
        {
            return new WslEnvironmentInfo(
                configuredDistroName,
                configuredUserName,
                $"/home/{configuredUserName}");
        }

        var cachedDefaultInfo = TryGetCachedDefaultWslEnvironment(database, out var cachedInfo)
            ? cachedInfo
            : null;

        if (configuredDistroName is null && configuredUserName is null && cachedDefaultInfo is not null)
        {
            return cachedDefaultInfo;
        }

        if (configuredDistroName is not null && cachedDefaultInfo is not null)
        {
            return new WslEnvironmentInfo(
                configuredDistroName,
                cachedDefaultInfo.UserName,
                cachedDefaultInfo.HomeDirectory);
        }

        if (configuredUserName is not null && cachedDefaultInfo is not null)
        {
            return new WslEnvironmentInfo(
                cachedDefaultInfo.DistroName,
                configuredUserName,
                $"/home/{configuredUserName}");
        }

        return _wsl.ResolveEnvironment(database.WslDistroName, database.WslUserName);
    }

    private static string SanitizeDirectorySegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch));
    }

    private static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record CodexTargetContext(string DisplayName, string CodexDirectoryPath, string BackupsDirectoryPath);
}

public sealed record CodexTargetDirectoryInfo(string DisplayName, string Path);
