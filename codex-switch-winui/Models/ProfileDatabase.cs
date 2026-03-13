using System;
using System.Collections.Generic;

namespace codex_switch_winui.Models;

public sealed class ProfileDatabase
{
    public List<CodexProfile> Profiles { get; set; } = new();
    public Guid? LastSelectedProfileId { get; set; }

    public bool ReplaceWindowsTarget { get; set; } = true;
    public bool ReplaceWslTarget { get; set; }
    public string? WslDistroName { get; set; }
    public string? WslUserName { get; set; }
    public string? CachedDefaultWslDistroName { get; set; }
    public string? CachedDefaultWslUserName { get; set; }
    public string? CachedDefaultWslHomeDirectory { get; set; }
    public DateTime? CachedDefaultWslDetectedAtUtc { get; set; }
    public string? CachedDefaultWslErrorMessage { get; set; }
    public DateTime? CachedDefaultWslErrorAtUtc { get; set; }

    public int SessionMigrationDays { get; set; } = 3;
}
