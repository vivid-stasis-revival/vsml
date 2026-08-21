namespace vividstasisModLoader;

public sealed class VsmlVersionInfo
{
    public int AbiVersion { get; init; }
    public string Version { get; init; } = string.Empty;
    public string Runtime { get; init; } = string.Empty;
}

public sealed class VsmlInstallRequest
{
    public int ApiVersion { get; set; } = VsmlApi.AbiVersion;
    public string RequestId { get; set; } = string.Empty;
    public string GameDirectory { get; set; } = string.Empty;
    public List<VsmlModRequest> Mods { get; set; } = [];
    public VsmlInstallOptions Options { get; set; } = new();
}

public sealed class VsmlModRequest
{
    public string Path { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

public sealed class VsmlInstallOptions
{
    public bool Backup { get; set; } = true;
    public bool RestoreBeforeApply { get; set; } = true;
    public bool DryRun { get; set; }
}

public sealed class VsmlRestoreRequest
{
    public int ApiVersion { get; set; } = VsmlApi.AbiVersion;
    public string RequestId { get; set; } = string.Empty;
    public string GameDirectory { get; set; } = string.Empty;
    public bool DryRun { get; set; }
}

public sealed class VsmlError
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public string? Mod { get; init; }
    public string? Entry { get; init; }
    public string? File { get; init; }
}

public sealed class VsmlInstallResult
{
    public bool Ok { get; init; }
    public VsmlError? Error { get; init; }
    public List<string> Warnings { get; init; } = [];
    public List<string> ChangedFiles { get; init; } = [];
    public List<VsmlModResult> Mods { get; init; } = [];
    public List<VsmlLogEntry> Logs { get; init; } = [];
    public long DurationMs { get; init; }
}

public sealed class VsmlRestoreResult
{
    public bool Ok { get; init; }
    public VsmlError? Error { get; init; }
    public List<string> Warnings { get; init; } = [];
    public List<string> ChangedFiles { get; init; } = [];
    public List<VsmlLogEntry> Logs { get; init; } = [];
    public long DurationMs { get; init; }
}

public sealed class VsmlReviewResult
{
    public bool Ok { get; init; }
    public VsmlError? Error { get; init; }
    public List<string> Warnings { get; init; } = [];
    public List<VsmlModReview> Mods { get; init; } = [];
    public long DurationMs { get; init; }
}

public sealed class VsmlValidationResult
{
    public bool Ok { get; init; }
    public VsmlError? Error { get; init; }
    public string GameDirectory { get; init; } = string.Empty;
    public string DataFile { get; init; } = string.Empty;
    public string VersionFile { get; init; } = string.Empty;
    public string GameVersion { get; init; } = string.Empty;
    public long DataFileSize { get; init; }
    public string DataSha256 { get; init; } = string.Empty;
    public bool ParsedSuccessfully { get; init; }
    public List<string> Warnings { get; init; } = [];
    public List<VsmlLogEntry> Logs { get; init; } = [];
    public long DurationMs { get; init; }
}

public sealed class VsmlModResult
{
    public string Path { get; init; } = string.Empty;
    public bool Ok { get; init; }
}

public sealed class VsmlModReview
{
    public string Path { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public bool Exists { get; init; }
    public List<string> Resources { get; init; } = [];
    public int CodeFileCount { get; init; }
    public int CodePatchCount { get; init; }
    public List<string> Warnings { get; init; } = [];
}
