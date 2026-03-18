using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace codex_switch_winui.Services;

public enum ProviderConnectionTestStatus
{
    Success,
    Warning,
    Failure
}

public readonly record struct ProviderConnectionTestResult(ProviderConnectionTestStatus Status, string Message);

public sealed class ProviderConnectionTestService
{
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly Regex BaseUrlLineRegex = new(
        "^\\s*base_url\\s*=\\s*\"(?<value>(?:[^\"\\\\]|\\\\.)*)\"",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    public bool TryReadBaseUrlFromConfigToml(string configTomlPath, out string? baseUrl, out string errorMessage)
    {
        baseUrl = null;

        if (string.IsNullOrWhiteSpace(configTomlPath))
        {
            errorMessage = "请先选择 config.toml。";
            return false;
        }

        if (!File.Exists(configTomlPath))
        {
            errorMessage = "选中的 config.toml 不存在。";
            return false;
        }

        try
        {
            var content = File.ReadAllText(configTomlPath);
            var match = BaseUrlLineRegex.Match(content);
            if (!match.Success)
            {
                errorMessage = "没有在 config.toml 里找到 base_url。请确认当前提供商配置里包含这个字段。";
                return false;
            }

            var encodedValue = $"\"{match.Groups["value"].Value}\"";
            baseUrl = JsonSerializer.Deserialize<string>(encodedValue)?.Trim();
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                errorMessage = "config.toml 里的 base_url 为空。";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }
        catch (JsonException)
        {
            errorMessage = "config.toml 里的 base_url 格式不正确。";
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public bool TryReadApiKeyFromAuthJson(string authJsonPath, out string? apiKey, out string errorMessage)
    {
        apiKey = null;

        if (string.IsNullOrWhiteSpace(authJsonPath))
        {
            errorMessage = "请先选择 auth.json。";
            return false;
        }

        if (!File.Exists(authJsonPath))
        {
            errorMessage = "选中的 auth.json 不存在。";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(authJsonPath));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                errorMessage = "auth.json 不是有效的对象结构。";
                return false;
            }

            if (!document.RootElement.TryGetProperty("OPENAI_API_KEY", out var apiKeyElement)
                || apiKeyElement.ValueKind != JsonValueKind.String)
            {
                errorMessage = "auth.json 里没有找到 OPENAI_API_KEY。";
                return false;
            }

            apiKey = apiKeyElement.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                errorMessage = "auth.json 里的 OPENAI_API_KEY 为空。";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }
        catch (JsonException)
        {
            errorMessage = "auth.json 不是有效的 JSON。";
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public async Task<ProviderConnectionTestResult> TestAsync(string baseUrl, string apiKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        HttpStatusCode? lastStatusCode = null;
        string? lastResponseBody = null;

        foreach (var endpoint in GetCandidateEndpoints(baseUrl))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await HttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                var responseBody = response.Content is null
                    ? string.Empty
                    : await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return CreateSuccessResult(responseBody);
                }

                lastStatusCode = response.StatusCode;
                lastResponseBody = responseBody;

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    continue;
                }

                return CreateFailureResult(response.StatusCode, responseBody);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new ProviderConnectionTestResult(
                    ProviderConnectionTestStatus.Failure,
                    "测试超时了。请检查 Base URL 是否正确，或稍后重试。");
            }
            catch (HttpRequestException ex)
            {
                return new ProviderConnectionTestResult(
                    ProviderConnectionTestStatus.Failure,
                    $"连接失败：{ex.Message}");
            }
        }

        return CreateFailureResult(lastStatusCode ?? HttpStatusCode.NotFound, lastResponseBody);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        return client;
    }

    private static IEnumerable<Uri> GetCandidateEndpoints(string baseUrl)
    {
        var normalizedBaseUrl = baseUrl.Trim().TrimEnd('/');
        if (normalizedBaseUrl.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
        {
            yield return new Uri(normalizedBaseUrl, UriKind.Absolute);
            yield break;
        }

        yield return new Uri($"{normalizedBaseUrl}/models", UriKind.Absolute);

        if (!normalizedBaseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            yield return new Uri($"{normalizedBaseUrl}/v1/models", UriKind.Absolute);
        }
    }

    private static ProviderConnectionTestResult CreateSuccessResult(string responseBody)
    {
        var modelCount = TryGetModelCount(responseBody);
        if (modelCount is > 0)
        {
            return new ProviderConnectionTestResult(
                ProviderConnectionTestStatus.Success,
                $"连接成功，已拿到 {modelCount.Value} 个模型。");
        }

        return new ProviderConnectionTestResult(
            ProviderConnectionTestStatus.Success,
            "连接成功，当前提供商可用。");
    }

    private static ProviderConnectionTestResult CreateFailureResult(HttpStatusCode statusCode, string? responseBody)
    {
        var detail = TryGetResponseMessage(responseBody);
        return statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new ProviderConnectionTestResult(
                ProviderConnectionTestStatus.Failure,
                AppendDetail("连接失败：认证没有通过，请检查 API Key 或 auth.json。", detail)),
            HttpStatusCode.TooManyRequests => new ProviderConnectionTestResult(
                ProviderConnectionTestStatus.Warning,
                AppendDetail("连接到了提供商，但当前被限流。一般说明地址和认证信息是通的。", detail)),
            HttpStatusCode.NotFound => new ProviderConnectionTestResult(
                ProviderConnectionTestStatus.Failure,
                AppendDetail("连接失败：没有找到模型接口，请检查 Base URL 是否正确。", detail)),
            _ => new ProviderConnectionTestResult(
                ProviderConnectionTestStatus.Failure,
                AppendDetail($"连接失败：服务返回 {(int)statusCode} {statusCode}。", detail))
        };
    }

    private static int? TryGetModelCount(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("data", out var dataElement)
                || dataElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return dataElement.GetArrayLength();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryGetResponseMessage(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (document.RootElement.TryGetProperty("error", out var errorElement))
            {
                if (errorElement.ValueKind == JsonValueKind.Object
                    && errorElement.TryGetProperty("message", out var nestedMessage)
                    && nestedMessage.ValueKind == JsonValueKind.String)
                {
                    return nestedMessage.GetString();
                }

                if (errorElement.ValueKind == JsonValueKind.String)
                {
                    return errorElement.GetString();
                }
            }

            if (document.RootElement.TryGetProperty("message", out var messageElement)
                && messageElement.ValueKind == JsonValueKind.String)
            {
                return messageElement.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string AppendDetail(string message, string? detail) =>
        string.IsNullOrWhiteSpace(detail)
            ? message
            : $"{message} 详情：{detail}";
}
