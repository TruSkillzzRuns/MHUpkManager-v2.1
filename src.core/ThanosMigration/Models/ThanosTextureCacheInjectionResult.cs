namespace OmegaAssetStudio.ThanosMigration.Models;

public sealed class ThanosTextureCacheInjectionResult
{
    public string SourceCandidateMode { get; set; } = "unknown";

    public bool PreflightPassed { get; set; }

    public int PreflightSourceChunks { get; set; }

    public int PreflightTargetChunks { get; set; }

    public int SourceCandidateEntries { get; set; }

    public int DependencySourceUpkCount { get; set; }

    public int DependencyTextureSignalCount { get; set; }

    public int SourceMatchedEntries { get; set; }

    public int TargetEntriesBefore { get; set; }

    public int TargetEntriesAfter { get; set; }

    public int AddedTextureEntries { get; set; }

    public int PatchedTextureEntries { get; set; }

    public int ReplacedInvalidChunks { get; set; }

    public int AddedChunks { get; set; }

    public int VerifiedChunks { get; set; }

    public int VerificationFailures { get; set; }

    public bool UsedTransactionalSwap { get; set; }

    public string TransactionDirectory { get; set; } = string.Empty;

    public List<string> TouchedTargetTfcFiles { get; } = [];

    public List<string> SwappedTargetPaths { get; } = [];

    public List<string> BackupPaths { get; } = [];

    public List<string> Warnings { get; } = [];

    public List<string> VerificationErrors { get; } = [];
}
