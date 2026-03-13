using System;
using System.Security.Cryptography;
using System.Text;

namespace codex_switch_winui.Services;

public static class ApiKeyProtection
{
    public static string ProtectToBase64(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        var bytes = Encoding.UTF8.GetBytes(apiKey);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string UnprotectFromBase64(string protectedBase64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedBase64);

        var protectedBytes = Convert.FromBase64String(protectedBase64);
        var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
}

