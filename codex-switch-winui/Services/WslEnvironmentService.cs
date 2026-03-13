using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace codex_switch_winui.Services;

public sealed class WslEnvironmentService
{
    public bool TryGetDefaultEnvironment(out WslEnvironmentInfo? info, out string errorMessage)
    {
        try
        {
            info = GetDefaultEnvironment();
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

    public bool TryResolveEnvironment(string? preferredDistroName, string? preferredUserName, out WslEnvironmentInfo? info, out string errorMessage)
    {
        try
        {
            info = ResolveEnvironment(preferredDistroName, preferredUserName);
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

    public WslEnvironmentInfo GetDefaultEnvironment()
    {
        var distroName = RunWslShell("printf '%s' \"$WSL_DISTRO_NAME\"");
        var userName = RunWslShell("printf '%s' \"$USER\"");
        var homeDirectory = RunWslShell("printf '%s' \"$HOME\"");

        if (string.IsNullOrWhiteSpace(distroName))
        {
            throw new InvalidOperationException("未能识别默认 WSL 发行版。");
        }

        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new InvalidOperationException("未能识别默认 WSL 用户。");
        }

        if (string.IsNullOrWhiteSpace(homeDirectory))
        {
            throw new InvalidOperationException("未能识别 WSL 家目录。");
        }

        return new WslEnvironmentInfo(
            distroName.Trim(),
            userName.Trim(),
            NormalizeLinuxPath(homeDirectory));
    }

    public WslEnvironmentInfo ResolveEnvironment(string? preferredDistroName, string? preferredUserName)
    {
        var configuredDistroName = NormalizeOptionalValue(preferredDistroName);
        var configuredUserName = NormalizeOptionalValue(preferredUserName);

        WslEnvironmentInfo? defaultInfo = null;
        if (configuredDistroName is null || configuredUserName is null)
        {
            defaultInfo = GetDefaultEnvironment();
        }

        var distroName = configuredDistroName ?? defaultInfo?.DistroName;
        var userName = configuredUserName ?? defaultInfo?.UserName;

        if (string.IsNullOrWhiteSpace(distroName))
        {
            throw new InvalidOperationException("未能识别 WSL 发行版，请先在设置中填写。");
        }

        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new InvalidOperationException("未能识别 WSL 用户名，请先在设置中填写。");
        }

        var homeDirectory = configuredUserName is null
            ? defaultInfo?.HomeDirectory ?? $"/home/{userName}"
            : $"/home/{userName}";

        return new WslEnvironmentInfo(
            distroName,
            userName,
            NormalizeLinuxPath(homeDirectory));
    }

    public string ToWindowsPath(string distroName, string linuxPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(distroName);
        ArgumentException.ThrowIfNullOrWhiteSpace(linuxPath);

        var normalizedLinuxPath = NormalizeLinuxPath(linuxPath);
        var relativePath = normalizedLinuxPath.TrimStart('/').Replace('/', '\\');
        return $@"\\wsl$\{distroName}\{relativePath}";
    }

    private static string RunWslShell(string command)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add("sh");
            startInfo.ArgumentList.Add("-lc");
            startInfo.ArgumentList.Add(command);

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();

            process.WaitForExit();
            Task.WaitAll(standardOutputTask, standardErrorTask);

            var standardOutput = CleanupProcessText(standardOutputTask.Result);
            var standardError = CleanupProcessText(standardErrorTask.Result);

            if (process.ExitCode != 0)
            {
                var message = string.IsNullOrWhiteSpace(standardError)
                    ? "wsl.exe 执行失败。"
                    : $"wsl.exe 执行失败：{standardError}";
                throw new InvalidOperationException(message);
            }

            if (string.IsNullOrWhiteSpace(standardOutput))
            {
                var message = string.IsNullOrWhiteSpace(standardError)
                    ? "wsl.exe 没有返回可用结果。"
                    : $"wsl.exe 没有返回可用结果：{standardError}";
                throw new InvalidOperationException(message);
            }

            return standardOutput;
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("未找到 wsl.exe，请先安装并初始化 WSL。", ex);
        }
    }

    private static string CleanupProcessText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Replace("\uFEFF", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static string NormalizeLinuxPath(string path)
    {
        var normalized = path.Trim().Replace('\\', '/');
        return normalized.StartsWith("/", StringComparison.Ordinal) ? normalized : "/" + normalized;
    }

    private static string? NormalizeOptionalValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}

public sealed record WslEnvironmentInfo(string DistroName, string UserName, string HomeDirectory);
