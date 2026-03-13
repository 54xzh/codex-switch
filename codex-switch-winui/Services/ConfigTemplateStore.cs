using System;
using System.IO;
using System.Text;

namespace codex_switch_winui.Services;

public sealed class ConfigTemplateStore
{
    public string RootPath { get; }
    public string TemplatesPath { get; }
    public string OpenAiTemplatePath { get; }
    public string ApiKeyTemplatePath { get; }

    public ConfigTemplateStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        RootPath = Path.Combine(appData, "codex-switch");
        TemplatesPath = Path.Combine(RootPath, "templates");
        OpenAiTemplatePath = Path.Combine(TemplatesPath, "config_openai.toml");
        ApiKeyTemplatePath = Path.Combine(TemplatesPath, "config_apikey.toml");
    }

    public string LoadOpenAiTemplate() => LoadOrCreate(OpenAiTemplatePath, GetDefaultOpenAiTemplate());

    public string LoadApiKeyTemplate()
    {
        var template = LoadOrCreate(ApiKeyTemplatePath, GetDefaultApiKeyTemplate());
        var legacyDefault = NormalizeLineEndings(GetLegacyDefaultApiKeyTemplate());
        var previousVariableDefault = NormalizeLineEndings(GetQuotedProviderNameApiKeyTemplate());

        if (!string.Equals(template, legacyDefault, StringComparison.Ordinal)
            && !string.Equals(template, previousVariableDefault, StringComparison.Ordinal))
        {
            return template;
        }

        var upgradedTemplate = NormalizeLineEndings(GetDefaultApiKeyTemplate());
        Save(ApiKeyTemplatePath, upgradedTemplate);
        return upgradedTemplate;
    }

    public void SaveOpenAiTemplate(string template) => Save(OpenAiTemplatePath, template);

    public void SaveApiKeyTemplate(string template) => Save(ApiKeyTemplatePath, template);

    public void ResetOpenAiTemplate() => Save(OpenAiTemplatePath, GetDefaultOpenAiTemplate());

    public void ResetApiKeyTemplate() => Save(ApiKeyTemplatePath, GetDefaultApiKeyTemplate());

    public string GetDefaultOpenAiTemplate() =>
        string.Join(
            "\n",
            new[]
            {
                "model = \"gpt-5.4\"",
                "model_reasoning_effort = \"xhigh\""
            }) + "\n";

    public string GetDefaultApiKeyTemplate() =>
        string.Join(
            "\n",
            new[]
            {
                "model_provider = {provider_name}",
                "model = \"gpt-5.4\"",
                "model_reasoning_effort = \"xhigh\"",
                string.Empty,
                "disable_response_storage = true",
                string.Empty,
                "[model_providers.{provider_key}]",
                "name = {provider_name}",
                "base_url = {base_url}",
                "wire_api = \"responses\"",
                "requires_openai_auth = true"
            }) + "\n";

    private static string GetQuotedProviderNameApiKeyTemplate() =>
        string.Join(
            "\n",
            new[]
            {
                "model_provider = {provider_name}",
                "model = \"gpt-5.4\"",
                "model_reasoning_effort = \"xhigh\"",
                string.Empty,
                "disable_response_storage = true",
                string.Empty,
                "[model_providers.{provider_name}]",
                "name = {provider_name}",
                "base_url = {base_url}",
                "wire_api = \"responses\"",
                "requires_openai_auth = true"
            }) + "\n";

    private static string GetLegacyDefaultApiKeyTemplate() =>
        string.Join(
            "\n",
            new[]
            {
                "model_provider = \"right\"",
                "model = \"gpt-5.4\"",
                "model_reasoning_effort = \"xhigh\"",
                string.Empty,
                "disable_response_storage = true",
                string.Empty,
                "[model_providers.right]",
                "name = \"right\"",
                "base_url = {base_url}",
                "wire_api = \"responses\"",
                "requires_openai_auth = true"
            }) + "\n";

    private string LoadOrCreate(string path, string defaultValue)
    {
        Directory.CreateDirectory(TemplatesPath);

        if (!File.Exists(path))
        {
            var normalized = NormalizeLineEndings(defaultValue);
            File.WriteAllText(path, normalized, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return normalized;
        }

        return NormalizeLineEndings(File.ReadAllText(path));
    }

    private void Save(string path, string template)
    {
        Directory.CreateDirectory(TemplatesPath);
        var normalized = NormalizeLineEndings(template ?? string.Empty);
        File.WriteAllText(path, normalized, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string NormalizeLineEndings(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return content ?? string.Empty;
        }

        return content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
    }
}
