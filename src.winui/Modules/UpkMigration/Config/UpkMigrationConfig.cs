namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.Configuration;

public sealed class UpkMigrationConfig
{
    public string GameRoot152 { get; set; } = string.Empty;

    public string GameRoot148 { get; set; } = string.Empty;

    public string OutputRoot { get; set; } = string.Empty;

    public string SipPath152 { get; set; } = string.Empty;

    public string MigrationRulesPath { get; set; } = string.Empty;

    public string LogPath { get; set; } = string.Empty;

    public string ResourcePrototypeSourcePath { get; set; } = string.Empty;

    public string ResourcePrototypeOutputRoot { get; set; } = string.Empty;

    public string ThanosTextureSourceRoot148 { get; set; } = string.Empty;

    public string BackportSourceRoot { get; set; } = string.Empty;

    public string BackportServerEmuRoot { get; set; } = string.Empty;

    public string BackportOutputRoot { get; set; } = string.Empty;

    public string BackportTargetRoot { get; set; } = string.Empty;

    public string BackportLogPath { get; set; } = string.Empty;

    public bool BackportRefreshPackageIndex { get; set; } = true;

    public string ClientMapDeploySourcePath { get; set; } = string.Empty;

    public string ClientMapDeployTargetRoot { get; set; } = string.Empty;

    public string ClientMapDeployFileName { get; set; } = string.Empty;

    public string ClientMapDeployMapName { get; set; } = string.Empty;

    public bool ClientMapDeployRefreshPackageIndex { get; set; }

    public Dictionary<string, string> ClientMapUpkMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> PrototypeUpkMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
