namespace OmegaAssetStudio.ThanosMigration.Models;

public sealed class ThanosPrototypeSource
{
    public ThanosDependencyItem Dependency { get; set; } = new();

    public string SourceUpkPath { get; set; } = string.Empty;

    public int ExportIndex { get; set; }

    public string ExportObjectName { get; set; } = string.Empty;

    public string ExportClassName { get; set; } = string.Empty;

    public string ExportOuterName { get; set; } = string.Empty;

    public int MatchScore { get; set; }

    public string MatchReason { get; set; } = string.Empty;

    public bool IsRaidRelevant { get; set; } = true;

    public string RaidReason { get; set; } = string.Empty;

    public bool IsRequiredRaidRoot { get; set; }
}

