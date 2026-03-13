using System;

namespace codex_switch_winui.Models;

public sealed class CodexProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;

    public ProviderCategory ProviderCategory { get; set; } = ProviderCategory.ApiKey;

    public CodexAuthMode AuthMode { get; set; } = CodexAuthMode.AuthJsonFile;
    public string? StoredAuthJsonPath { get; set; }
    public string? ProtectedApiKeyBase64 { get; set; }
    public string? StoredConfigTomlPath { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
