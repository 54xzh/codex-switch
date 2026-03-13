using System.Text.RegularExpressions;
using codex_switch_winui.Models;

namespace codex_switch_winui.Services;

public static partial class ApiKeyProviderNameRules
{
    public static string NormalizeOrDefault(string? value) =>
        string.IsNullOrWhiteSpace(value) ? ProfileDatabase.DefaultApiKeyProviderName : value.Trim();

    public static bool IsValidBareKey(string? value) =>
        BareKeyRegex().IsMatch(NormalizeOrDefault(value));

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex BareKeyRegex();
}
