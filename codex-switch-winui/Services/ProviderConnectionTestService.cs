using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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

    public Uri BuildResponsesEndpoint(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        var normalizedBaseUrl = baseUrl.Trim().TrimEnd('/');
        var endpoint = normalizedBaseUrl.EndsWith("/responses", StringComparison.OrdinalIgnoreCase)
            ? normalizedBaseUrl
            : $"{normalizedBaseUrl}/responses";
        return new Uri(endpoint, UriKind.Absolute);
    }

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

    public async Task<ProviderConnectionTestResult> TestResponsesAsync(
        string baseUrl,
        string apiKey,
        string model,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        try
        {
            var endpoint = BuildResponsesEndpoint(baseUrl);
            var firstAttempt = await SendResponsesRequestAsync(
                endpoint,
                apiKey,
                model,
                includeMaxOutputTokens: true,
                cancellationToken);

            if (ShouldRetryWithoutMaxOutputTokens(firstAttempt.StatusCode, firstAttempt.ResponseBody))
            {
                var fallbackAttempt = await SendResponsesRequestAsync(
                    endpoint,
                    apiKey,
                    model,
                    includeMaxOutputTokens: false,
                    cancellationToken);
                return CreateResult(fallbackAttempt.StatusCode, fallbackAttempt.ResponseBody);
            }

            return CreateResult(firstAttempt.StatusCode, firstAttempt.ResponseBody);
        }
        catch (UriFormatException)
        {
            return new ProviderConnectionTestResult(
                ProviderConnectionTestStatus.Failure,
                "连接失败：Base URL 格式不正确。");
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

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        return client;
    }

    private static async Task<ResponsesAttempt> SendResponsesRequestAsync(
        Uri endpoint,
        string apiKey,
        string model,
        bool includeMaxOutputTokens,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var input = new object[]
        {
            new
            {
                role = "user",
                content = new object[]
                {
                    new
                    {
                        type = "input_text",
                        text = "ping"
                    }
                }
            }
        };

        var payload = includeMaxOutputTokens
            ? JsonSerializer.Serialize(new
            {
                model = model.Trim(),
                input,
                max_output_tokens = 1
            })
            : JsonSerializer.Serialize(new
            {
                model = model.Trim(),
                input
            });
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var responseBody = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);
        return new ResponsesAttempt(response.StatusCode, responseBody);
    }

    private static ProviderConnectionTestResult CreateResult(HttpStatusCode statusCode, string? responseBody)
    {
        if ((int)statusCode >= 200 && (int)statusCode <= 299)
        {
            return new ProviderConnectionTestResult(
                ProviderConnectionTestStatus.Success,
                "连接成功，测试模型可用。");
        }

        var detail = TryGetResponseMessage(responseBody);
        if (LooksLikeModelIssue(statusCode, detail, responseBody))
        {
            return new ProviderConnectionTestResult(
                ProviderConnectionTestStatus.Warning,
                AppendDetail("已经连到服务，但测试模型不可用。你可以换一个测试模型再试。", detail));
        }

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
                AppendDetail("连接失败：没有找到 responses 接口，请检查 Base URL 是否正确。", detail)),
            _ => new ProviderConnectionTestResult(
                ProviderConnectionTestStatus.Failure,
                AppendDetail($"连接失败：服务返回 {(int)statusCode} {statusCode}。", detail))
        };
    }

    private static bool ShouldRetryWithoutMaxOutputTokens(HttpStatusCode statusCode, string? responseBody)
    {
        if (statusCode != HttpStatusCode.BadRequest || string.IsNullOrWhiteSpace(responseBody))
        {
            return false;
        }

        var normalized = responseBody.ToLowerInvariant();
        return normalized.Contains("max_output_tokens", StringComparison.Ordinal)
            || normalized.Contains("unknown parameter", StringComparison.Ordinal)
            || normalized.Contains("unknown field", StringComparison.Ordinal)
            || normalized.Contains("additional properties", StringComparison.Ordinal)
            || normalized.Contains("extra inputs are not permitted", StringComparison.Ordinal);
    }

    private static bool LooksLikeModelIssue(HttpStatusCode statusCode, string? detail, string? responseBody)
    {
        if (statusCode is not (HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity))
        {
            return false;
        }

        var normalized = $"{detail}\n{responseBody}".ToLowerInvariant();
        if (!normalized.Contains("model", StringComparison.Ordinal))
        {
            return false;
        }

        return normalized.Contains("not found", StringComparison.Ordinal)
            || normalized.Contains("does not exist", StringComparison.Ordinal)
            || normalized.Contains("unknown model", StringComparison.Ordinal)
            || normalized.Contains("unsupported", StringComparison.Ordinal)
            || normalized.Contains("not available", StringComparison.Ordinal)
            || normalized.Contains("invalid model", StringComparison.Ordinal)
            || normalized.Contains("无效模型", StringComparison.Ordinal)
            || normalized.Contains("模型不存在", StringComparison.Ordinal)
            || normalized.Contains("不支持", StringComparison.Ordinal);
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

            if (document.RootElement.TryGetProperty("detail", out var detailElement)
                && detailElement.ValueKind == JsonValueKind.String)
            {
                return detailElement.GetString();
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

    private readonly record struct ResponsesAttempt(HttpStatusCode StatusCode, string ResponseBody);
}
