namespace OmegaAssetStudio.ThanosMigration.Models;

public sealed class ThanosTextureCacheRollbackResult
{
    public bool Restored { get; set; }

    public string Timestamp { get; set; } = string.Empty;

    public List<string> RestoredPaths { get; } = [];

    public List<string> MissingBackupPaths { get; } = [];
}
