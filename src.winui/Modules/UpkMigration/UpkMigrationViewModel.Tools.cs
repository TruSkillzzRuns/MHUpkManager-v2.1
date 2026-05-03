using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration;

public sealed partial class UpkMigrationViewModel
{
    private readonly UpkBackportService backportService = new();
    private readonly UpkDeploymentAssistantService deploymentService = new();

    private string backportLogPath = string.Empty;
    private string backportSourceRoot = string.Empty;
    private string backportServerEmuRoot = string.Empty;
    private string backportOutputRoot = string.Empty;
    private string backportTargetRoot = string.Empty;
    private bool backportRefreshPackageIndex = true;
    private double backportProgress;
    private string backportProgressText = "Backport progress: 0%";
    private string backportStatusText = "Ready.";
    private string backportSummaryText = "No backport has run yet.";
    private string deploySourceUpkPath = string.Empty;
    private string deployTargetClientRoot = string.Empty;
    private string deployFileName = string.Empty;
    private string deployClientMapName = string.Empty;
    private bool deployRefreshPackageIndex = true;
    private string deployStatusText = "Ready.";
    private string deploySummaryText = "No deployment has run yet.";

    public Func<Task<string?>>? BrowseBackportLogRequestedAsync { get; set; }

    public Func<Task<string?>>? BrowseBackportSourceRequestedAsync { get; set; }

    public Func<Task<string?>>? BrowseBackportServerEmuRequestedAsync { get; set; }

    public Func<Task<string?>>? BrowseBackportOutputRequestedAsync { get; set; }

    public Func<Task<string?>>? BrowseBackportTargetRequestedAsync { get; set; }

    public Func<Task<string?>>? BrowseDeploySourceRequestedAsync { get; set; }

    public Func<Task<string?>>? BrowseDeployTargetRequestedAsync { get; set; }

    public ICommand BrowseBackportLogCommand { get; private set; } = null!;

    public ICommand BrowseBackportSourceCommand { get; private set; } = null!;

    public ICommand BrowseBackportServerEmuCommand { get; private set; } = null!;

    public ICommand BrowseBackportOutputCommand { get; private set; } = null!;

    public ICommand FindMissingPackagesCommand { get; private set; } = null!;

    public ICommand BackportAndDeployCommand { get; private set; } = null!;

    public ICommand ClearCacheCommand { get; private set; } = null!;

    public ICommand BrowseBackportTargetCommand { get; private set; } = null!;

    public ICommand BrowseDeploySourceCommand { get; private set; } = null!;

    public ICommand BrowseDeployTargetCommand { get; private set; } = null!;

    public ICommand DeploySelectedUpkCommand { get; private set; } = null!;

    public ICommand ResetDeployFieldsCommand { get; private set; } = null!;

    public string BackportLogPath
    {
        get => backportLogPath;
        set
        {
            if (SetField(ref backportLogPath, value))
            {
                config.BackportLogPath = backportLogPath;
                UpkMigrationConfigStore.Save(config);
                UpdateBackportStatus($"Backport log set to {backportLogPath}.");
            }
        }
    }

    public string BackportSourceRoot
    {
        get => backportSourceRoot;
        set
        {
            if (SetField(ref backportSourceRoot, value))
            {
                config.BackportSourceRoot = backportSourceRoot;
                config.GameRoot148 = backportSourceRoot;
                UpkMigrationConfigStore.Save(config);
                UpdateBackportStatus($"1.48 source root set to {backportSourceRoot}.");
            }
        }
    }

    public string BackportServerEmuRoot
    {
        get => backportServerEmuRoot;
        set
        {
            if (SetField(ref backportServerEmuRoot, value))
            {
                config.BackportServerEmuRoot = backportServerEmuRoot;
                UpkMigrationConfigStore.Save(config);
                UpdateBackportStatus($"ServerEmu root set to {backportServerEmuRoot}.");
            }
        }
    }

    public string BackportOutputRoot
    {
        get => backportOutputRoot;
        set
        {
            if (SetField(ref backportOutputRoot, value))
            {
                config.BackportOutputRoot = backportOutputRoot;
                UpkMigrationConfigStore.Save(config);
                UpdateBackportStatus($"Backport output set to {backportOutputRoot}.");
            }
        }
    }

    public string BackportTargetRoot
    {
        get => backportTargetRoot;
        set
        {
            if (SetField(ref backportTargetRoot, value))
            {
                config.BackportTargetRoot = backportTargetRoot;
                config.GameRoot152 = backportTargetRoot;
                UpkMigrationConfigStore.Save(config);
                UpdateBackportStatus($"Target client root set to {backportTargetRoot}.");
            }
        }
    }

    public bool BackportRefreshPackageIndex
    {
        get => backportRefreshPackageIndex;
        set
        {
            if (SetField(ref backportRefreshPackageIndex, value))
            {
                config.BackportRefreshPackageIndex = backportRefreshPackageIndex;
                UpkMigrationConfigStore.Save(config);
            }
        }
    }

    public string BackportStatusText
    {
        get => backportStatusText;
        private set => SetField(ref backportStatusText, value);
    }

    public double BackportProgress
    {
        get => backportProgress;
        private set => SetField(ref backportProgress, value);
    }

    public string BackportProgressText
    {
        get => backportProgressText;
        private set => SetField(ref backportProgressText, value);
    }

    public string BackportSummaryText
    {
        get => backportSummaryText;
        private set => SetField(ref backportSummaryText, value);
    }

    public ObservableCollection<UpkBackportPackageStatusRow> BackportPackageStatuses { get; } = [];

    public string DeploySourceUpkPath
    {
        get => deploySourceUpkPath;
        set
        {
            if (SetField(ref deploySourceUpkPath, value))
            {
                config.ClientMapDeploySourcePath = deploySourceUpkPath;
                UpkMigrationConfigStore.Save(config);
                UpdateDeployStatus($"Deploy source set to {deploySourceUpkPath}.");
            }

            AutoPopulateDeployFieldsFromSource(deploySourceUpkPath);
        }
    }

    public string DeployTargetClientRoot
    {
        get => deployTargetClientRoot;
        set
        {
            if (SetField(ref deployTargetClientRoot, value))
            {
                config.ClientMapDeployTargetRoot = deployTargetClientRoot;
                config.GameRoot152 = deployTargetClientRoot;
                UpkMigrationConfigStore.Save(config);
                UpdateDeployStatus($"Target client root set to {deployTargetClientRoot}.");
            }
        }
    }

    public string DeployFileName
    {
        get => deployFileName;
        set
        {
            if (SetField(ref deployFileName, value))
            {
                config.ClientMapDeployFileName = deployFileName;
                UpkMigrationConfigStore.Save(config);
                UpdateDeployStatus($"Deploy file name set to {deployFileName}.");
            }
        }
    }

    public string DeployClientMapName
    {
        get => deployClientMapName;
        set
        {
            if (SetField(ref deployClientMapName, value))
            {
                config.ClientMapDeployMapName = deployClientMapName;
                UpkMigrationConfigStore.Save(config);
                UpdateDeployStatus($"ClientMap name set to {deployClientMapName}.");
            }
        }
    }

    public bool DeployRefreshPackageIndex
    {
        get => deployRefreshPackageIndex;
        set
        {
            if (SetField(ref deployRefreshPackageIndex, value))
            {
                config.ClientMapDeployRefreshPackageIndex = deployRefreshPackageIndex;
                UpkMigrationConfigStore.Save(config);
            }
        }
    }

    public string DeployStatusText
    {
        get => deployStatusText;
        private set => SetField(ref deployStatusText, value);
    }

    public string DeploySummaryText
    {
        get => deploySummaryText;
        private set => SetField(ref deploySummaryText, value);
    }

    private void InitializeToolingState()
    {
        backportLogPath = !string.IsNullOrWhiteSpace(config.BackportLogPath) ? config.BackportLogPath : config.LogPath;
        backportSourceRoot = !string.IsNullOrWhiteSpace(config.BackportSourceRoot) ? config.BackportSourceRoot : config.GameRoot148;
        backportServerEmuRoot = config.BackportServerEmuRoot;
        backportOutputRoot = !string.IsNullOrWhiteSpace(config.BackportOutputRoot) ? config.BackportOutputRoot : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "OmegaAssetStudio_UpkMigration_Backport");
        backportTargetRoot = !string.IsNullOrWhiteSpace(config.BackportTargetRoot) ? config.BackportTargetRoot : config.GameRoot152;
        backportRefreshPackageIndex = config.BackportRefreshPackageIndex;
        deploySourceUpkPath = config.ClientMapDeploySourcePath;
        deployTargetClientRoot = !string.IsNullOrWhiteSpace(config.ClientMapDeployTargetRoot) ? config.ClientMapDeployTargetRoot : config.GameRoot152;
        deployFileName = config.ClientMapDeployFileName;
        deployClientMapName = config.ClientMapDeployMapName;
        deployRefreshPackageIndex = config.ClientMapDeployRefreshPackageIndex;
        AutoPopulateDeployFieldsFromSource(deploySourceUpkPath);

        BrowseBackportLogCommand = new AsyncRelayCommand(BrowseBackportLogAsync);
        BrowseBackportSourceCommand = new AsyncRelayCommand(BrowseBackportSourceAsync);
        BrowseBackportServerEmuCommand = new AsyncRelayCommand(BrowseBackportServerEmuAsync);
        BrowseBackportOutputCommand = new AsyncRelayCommand(BrowseBackportOutputAsync);
        BrowseBackportTargetCommand = new AsyncRelayCommand(BrowseBackportTargetAsync);
        FindMissingPackagesCommand = new AsyncRelayCommand(FindMissingPackagesAsync);
        BackportAndDeployCommand = new AsyncRelayCommand(BackportAndDeployAsync);
        ClearCacheCommand = new AsyncRelayCommand(ClearCacheAsync);
        BrowseDeploySourceCommand = new AsyncRelayCommand(BrowseDeploySourceAsync);
        BrowseDeployTargetCommand = new AsyncRelayCommand(BrowseDeployTargetAsync);
        DeploySelectedUpkCommand = new AsyncRelayCommand(DeploySelectedUpkAsync);
        ResetDeployFieldsCommand = new AsyncRelayCommand(ResetDeployFieldsAsync);
    }

    private async Task BrowseBackportLogAsync()
    {
        if (BrowseBackportLogRequestedAsync is null)
            return;

        string? path = await BrowseBackportLogRequestedAsync().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path))
            BackportLogPath = path;
    }

    private async Task BrowseBackportSourceAsync()
    {
        if (BrowseBackportSourceRequestedAsync is null)
            return;

        string? path = await BrowseBackportSourceRequestedAsync().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path))
            BackportSourceRoot = path;
    }

    private async Task BrowseBackportServerEmuAsync()
    {
        if (BrowseBackportServerEmuRequestedAsync is null)
            return;

        string? path = await BrowseBackportServerEmuRequestedAsync().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path))
            BackportServerEmuRoot = path;
    }

    private async Task BrowseBackportOutputAsync()
    {
        if (BrowseBackportOutputRequestedAsync is null)
            return;

        string? path = await BrowseBackportOutputRequestedAsync().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path))
            BackportOutputRoot = path;
    }

    private async Task BrowseBackportTargetAsync()
    {
        if (BrowseBackportTargetRequestedAsync is null)
            return;

        string? path = await BrowseBackportTargetRequestedAsync().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path))
            BackportTargetRoot = path;
    }

    private async Task FindMissingPackagesAsync()
    {
        if (string.IsNullOrWhiteSpace(BackportLogPath))
        {
            UpdateBackportStatus("Select a log file before finding missing packages.");
            return;
        }

        if (string.IsNullOrWhiteSpace(BackportSourceRoot))
        {
            UpdateBackportStatus("Select a 1.48 source root before finding missing packages.");
            return;
        }

        string outputRoot = string.IsNullOrWhiteSpace(BackportOutputRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "OmegaAssetStudio_UpkMigration_Backport")
            : BackportOutputRoot;

        try
        {
            SetBackportProgress(0, "Scanning client log...");
            UpkBackportReport report = await backportService.FindMissingPackagesAsync(
                BackportLogPath,
                BackportSourceRoot,
                BackportServerEmuRoot,
                outputRoot,
                UpdateBackportStatus).ConfigureAwait(true);

            ApplyBackportReport(report);
            BackportSummaryText = report.SummaryText;
            BackportStatusText = "Missing package discovery complete.";
            SetBackportProgress(100, "Backport progress: 100%");
        }
        catch (Exception ex)
        {
            UpdateBackportStatus($"Backport failed: {ex.Message}");
        }
    }

    private async Task BackportAndDeployAsync()
    {
        if (string.IsNullOrWhiteSpace(DeployTargetClientRoot))
        {
            UpdateBackportStatus("Select a target client root before backporting and deploying.");
            return;
        }

        string outputRoot = string.IsNullOrWhiteSpace(BackportOutputRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "OmegaAssetStudio_UpkMigration_Backport")
            : BackportOutputRoot;

        try
        {
            SetBackportProgress(0, "Scanning client log...");
            UpkBackportReport report = await backportService.FindMissingPackagesAsync(
                BackportLogPath,
                BackportSourceRoot,
                BackportServerEmuRoot,
                outputRoot,
                UpdateBackportStatus).ConfigureAwait(true);

            SetBackportProgress(60, "Deploying backported packages...");
            int deployedCount = await DeployBackportedPackagesAsync(report, DeployTargetClientRoot, BackportRefreshPackageIndex).ConfigureAwait(true);
            ApplyBackportReport(report);
            BackportSummaryText = $"{report.SummaryText}  Deployed: {deployedCount:N0}";
            BackportStatusText = $"Backport and deploy complete. Output written to {outputRoot}.";
            SetBackportProgress(100, "Backport progress: 100%");
        }
        catch (Exception ex)
        {
            UpdateBackportStatus($"Backport and deploy failed: {ex.Message}");
        }
    }

    private async Task BrowseDeploySourceAsync()
    {
        if (BrowseDeploySourceRequestedAsync is null)
            return;

        string? path = await BrowseDeploySourceRequestedAsync().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path))
        {
            DeploySourceUpkPath = path;
            AutoPopulateDeployFieldsFromSource(path);
        }
    }

    private async Task BrowseDeployTargetAsync()
    {
        if (BrowseDeployTargetRequestedAsync is null)
            return;

        string? path = await BrowseDeployTargetRequestedAsync().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path))
            DeployTargetClientRoot = path;
    }

    private async Task DeploySelectedUpkAsync()
    {
        string sourcePath = !string.IsNullOrWhiteSpace(DeploySourceUpkPath)
            ? DeploySourceUpkPath
            : SelectedJob?.OutputUpkPath ?? SelectedJob?.SourceUpkPath ?? string.Empty;

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            UpdateDeployStatus("Select a migrated UPK before deploying.");
            return;
        }

        if (string.IsNullOrWhiteSpace(DeployTargetClientRoot))
        {
            UpdateDeployStatus("Select the target client root before deploying.");
            return;
        }

        if (string.IsNullOrWhiteSpace(DeployFileName))
        {
            DeployFileName = Path.GetFileName(sourcePath);
        }

        try
        {
            UpkDeploymentReport report = await deploymentService.DeployAsync(
                sourcePath,
                DeployTargetClientRoot,
                DeployFileName,
                DeployClientMapName,
                DeployRefreshPackageIndex,
                UpdateDeployStatus).ConfigureAwait(true);

            DeploySummaryText = report.SummaryText;
            DeployStatusText = report.Success
                ? $"Deployment complete. Output written to {report.DeployedPath}."
                : $"Deployment failed: {string.Join("; ", report.Errors)}";
        }
        catch (Exception ex)
        {
            UpdateDeployStatus($"Deployment failed: {ex.Message}");
        }
    }

    private Task ResetDeployFieldsAsync()
    {
        DeploySourceUpkPath = string.Empty;
        DeployTargetClientRoot = string.Empty;
        DeployFileName = string.Empty;
        DeployClientMapName = string.Empty;
        DeployRefreshPackageIndex = true;
        UpdateDeployStatus("Deploy fields cleared.");
        return Task.CompletedTask;
    }

    private Task ClearCacheAsync()
    {
        service.ClearCache();
        BackportPackageStatuses.Clear();
        BackportSummaryText = "Migration cache cleared.";
        BackportStatusText = "Migration cache cleared.";
        return Task.CompletedTask;
    }

    private async Task<int> DeployBackportedPackagesAsync(UpkBackportReport report, string targetRoot, bool refreshPackageIndex)
    {
        string fullTargetRoot = Path.GetFullPath(targetRoot);
        Directory.CreateDirectory(fullTargetRoot);

        int deployedCount = 0;
        foreach (string sourcePath in report.BackportedPackages.Where(File.Exists))
        {
            string destinationPath = Path.Combine(fullTargetRoot, Path.GetFileName(sourcePath));
            File.Copy(sourcePath, destinationPath, overwrite: true);
            deployedCount++;
            UpdateBackportStatus($"Deployed {Path.GetFileName(sourcePath)} to {fullTargetRoot}.");

            string packageName = Path.GetFileNameWithoutExtension(sourcePath);
            UpkBackportPackageStatusRow? row = report.PackageStatuses.FirstOrDefault(item => string.Equals(item.PackageName, packageName, StringComparison.OrdinalIgnoreCase));
            if (row is not null)
            {
                row.DeployedPath = destinationPath;
                row.Status = "Deployed";
            }
        }

        if (refreshPackageIndex)
        {
            string packageIndexPath = await deploymentService.RefreshPackageIndexAsync(fullTargetRoot, UpdateBackportStatus).ConfigureAwait(true);
            UpdateBackportStatus($"Backport package index refreshed: {packageIndexPath}");
        }

        return deployedCount;
    }

    private void ApplyBackportReport(UpkBackportReport report)
    {
        BackportPackageStatuses.Clear();
        foreach (UpkBackportPackageStatusRow row in report.PackageStatuses)
        {
            BackportPackageStatuses.Add(new UpkBackportPackageStatusRow
            {
                PackageName = row.PackageName,
                Status = row.Status,
                SourcePath = row.SourcePath,
                OutputPath = row.OutputPath,
                DeployedPath = row.DeployedPath
            });
        }
    }

    private void AutoPopulateDeployFieldsFromSource(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return;

        string mapName = Path.GetFileNameWithoutExtension(sourcePath);
        if (mapName.EndsWith("_152", StringComparison.OrdinalIgnoreCase))
            mapName = mapName[..^4];

        DeployFileName = $"{mapName}{Path.GetExtension(sourcePath)}";
        if (string.IsNullOrWhiteSpace(DeployClientMapName))
            DeployClientMapName = mapName;
    }

    private void UpdateBackportStatus(string message) => PostToUi(() => BackportStatusText = message);

    private void UpdateDeployStatus(string message) => PostToUi(() => DeployStatusText = message);

    private void SetBackportProgress(double value, string text) => PostToUi(() =>
    {
        BackportProgress = value;
        BackportProgressText = text;
    });
}