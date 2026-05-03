using System.Text.Json.Serialization;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;

public sealed class UpkBackportReport
{
    public string LogPath { get; set; } = string.Empty;

    public string SourceRoot { get; set; } = string.Empty;

    public string? ServerEmuRoot { get; set; }

    public string OutputRoot { get; set; } = string.Empty;

    public List<string> DetectedPackages { get; } = [];

    public List<string> ExpandedPackages { get; } = [];

    public List<string> BackportedPackages { get; } = [];

    public List<string> MissingPackages { get; } = [];

    public List<string> SkippedPackages { get; } = [];

    public List<UpkBackportPackageStatusRow> PackageStatuses { get; } = [];

    public List<string> Notes { get; } = [];

    [JsonIgnore]
    public int DetectedCount => DetectedPackages.Count;

    [JsonIgnore]
    public int BackportedCount => BackportedPackages.Count;

    [JsonIgnore]
    public string SummaryText =>
        $"Detected: {DetectedCount}  Backported: {BackportedCount}  Missing: {MissingPackages.Count}  Skipped: {SkippedPackages.Count}";
}