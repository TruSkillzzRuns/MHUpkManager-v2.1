using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using OmegaAssetStudio.ThanosMigration.Models;
using OmegaAssetStudio.ThanosMigration.Services;
using OmegaAssetStudio.TextureManager;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Configuration;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.ResourceScanning;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration;

public sealed partial class UpkMigrationViewModel : INotifyPropertyChanged
{
    private readonly DispatcherQueue? dispatcherQueue;
    private readonly UpkMigrationService service;
    private readonly UpkMigrationConfig config;
    private readonly ResourcePrototypeScannerService resourcePrototypeScanner = new();
    private string outputDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "OmegaAssetStudio_UpkMigration");
    private string resourcePrototypeSourcePath = string.Empty;
    private string resourcePrototypeOutputDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "OmegaAssetStudio_UpkMigration_ResourceScanner");
    private string statusText = "Ready.";
    private string progressText = "Ready. Select UPKs for migration, or use Thanos texture injection.";
    private double overallProgress;
    private string resultsSummaryText = "No migration has run yet.";
    private string resourcePrototypeStatusText = "Ready.";
    private string resourcePrototypeSummaryText = "No resource prototype scan has run yet.";
    private string selectedJobDetails = "No job selected.";
    private string logFilter = string.Empty;
    private string textureManifestDirectory = string.Empty;
    private string textureSourceDirectory148 = string.Empty;
    private double textureInjectProgress;
    private string textureInjectProgressText = "Injection progress: 0%";
    private MigrationMode currentMode = MigrationMode.Standard;
    private ThanosMigrationReport? thanosReport;
    private MigrationJob? selectedJob;
    private MigrationLogEntry? selectedLogEntry;
    private bool isBusy;
    private bool isThanosBusy;
    private bool isResourcePrototypeScanning;
    private readonly ThanosStructuralMigrationService thanosStructuralService;
    private readonly ThanosTextureMigrationService thanosTextureService;
    private readonly ThanosTextureCacheInjectorService thanosTextureCacheInjectorService;
    private readonly TfcManifestService tfcManifestService;
    private readonly ThanosPrototypeMergerViewModel prototypeMerger;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<MigrationJob> Jobs { get; } = [];

    public ObservableCollection<MigrationLogEntry> LogEntries { get; } = [];

    public ObservableCollection<ClientMapDependency> ClientMapDependencies { get; } = [];

    public ThanosPrototypeMergerViewModel PrototypeMerger => prototypeMerger;

    public Func<Task<IReadOnlyList<string>>>? SelectUpksRequestedAsync { get; set; }

    public Func<Task<string?>>? BrowseOutputDirectoryRequestedAsync { get; set; }

    public Func<Task<string?>>? BrowseTextureManifestDirectoryRequestedAsync { get; set; }

    public Func<Task<string?>>? BrowseTextureSourceDirectoryRequestedAsync { get; set; }

    public Func<Task<string?>>? BrowseResourcePrototypeSourceRequestedAsync { get; set; }

    public Func<Task<string?>>? BrowseResourcePrototypeOutputRequestedAsync { get; set; }

    public ICommand SelectUpksCommand { get; }

    public ICommand StartMigrationCommand { get; }

    public ICommand BrowseOutputDirectoryCommand { get; }

    public ICommand BrowseTextureManifestDirectoryCommand { get; }

    public ICommand BrowseTextureSourceDirectoryCommand { get; }

    public ICommand AnalyzeThanosCommand { get; }

    public ICommand MigrateThanosCommand { get; }

    public ICommand UpdateTextureManifestCommand { get; }

    public ICommand InjectTextureCachePayloadsCommand { get; }

    public ICommand RunKnowhereProfileCommand { get; }

    public ICommand RollbackTextureCacheCommand { get; }

    public ICommand BrowseResourcePrototypeSourceCommand { get; }

    public ICommand BrowseResourcePrototypeOutputCommand { get; }

    public ICommand ScanResourcePrototypesCommand { get; }

    public MigrationJob? SelectedJob
    {
        get => selectedJob;
        set
        {
            if (ReferenceEquals(selectedJob, value))
                return;

            if (selectedJob is not null)
                selectedJob.PropertyChanged -= SelectedJob_PropertyChanged;

            selectedJob = value;

            if (selectedJob is not null)
                selectedJob.PropertyChanged += SelectedJob_PropertyChanged;

            OnPropertyChanged();
            RefreshSelectedJobDetails();
            RefreshThanosReport();
        }
    }

    public MigrationLogEntry? SelectedLogEntry
    {
        get => selectedLogEntry;
        set => SetField(ref selectedLogEntry, value);
    }

    public string SelectedJobDetails
    {
        get => selectedJobDetails;
        private set => SetField(ref selectedJobDetails, value);
    }

    public string OutputDirectory
    {
        get => outputDirectory;
        set
        {
            if (SetField(ref outputDirectory, value))
            {
                config.OutputRoot = outputDirectory;
                UpkMigrationConfigStore.Save(config);
                RefreshJobOutputPaths();
                UpdateStatus($"Output directory set to {outputDirectory}.");
            }
        }
    }

    public string ResourcePrototypeSourcePath
    {
        get => resourcePrototypeSourcePath;
        set
        {
            if (SetField(ref resourcePrototypeSourcePath, value))
            {
                config.ResourcePrototypeSourcePath = resourcePrototypeSourcePath;
                config.SipPath152 = resourcePrototypeSourcePath;
                UpkMigrationConfigStore.Save(config);
                UpdateResourcePrototypeStatus($"Resource prototype source set to {resourcePrototypeSourcePath}.");
            }
        }
    }

    public string ResourcePrototypeOutputDirectory
    {
        get => resourcePrototypeOutputDirectory;
        set
        {
            if (SetField(ref resourcePrototypeOutputDirectory, value))
            {
                config.ResourcePrototypeOutputRoot = resourcePrototypeOutputDirectory;
                UpkMigrationConfigStore.Save(config);
                UpdateResourcePrototypeStatus($"Resource prototype output set to {resourcePrototypeOutputDirectory}.");
            }
        }
    }

    public string TextureManifestDirectory
    {
        get => textureManifestDirectory;
        set
        {
            if (SetField(ref textureManifestDirectory, value))
                UpdateStatus($"Texture manifest folder set to {textureManifestDirectory}.");
        }
    }

    public string TextureSourceDirectory148
    {
        get => textureSourceDirectory148;
        set
        {
            if (SetField(ref textureSourceDirectory148, value))
            {
                config.ThanosTextureSourceRoot148 = textureSourceDirectory148;
                UpkMigrationConfigStore.Save(config);
                UpdateStatus($"1.48 texture source folder set to {textureSourceDirectory148}.");
            }
        }
    }

    public double TextureInjectProgress
    {
        get => textureInjectProgress;
        private set => SetField(ref textureInjectProgress, value);
    }

    public string TextureInjectProgressText
    {
        get => textureInjectProgressText;
        private set => SetField(ref textureInjectProgressText, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetField(ref statusText, value);
    }

    public string ProgressText
    {
        get => progressText;
        private set => SetField(ref progressText, value);
    }

    public double OverallProgress
    {
        get => overallProgress;
        private set => SetField(ref overallProgress, value);
    }

    public string ResultsSummaryText
    {
        get => resultsSummaryText;
        private set => SetField(ref resultsSummaryText, value);
    }

    public string ResourcePrototypeStatusText
    {
        get => resourcePrototypeStatusText;
        private set => SetField(ref resourcePrototypeStatusText, value);
    }

    public string ResourcePrototypeSummaryText
    {
        get => resourcePrototypeSummaryText;
        private set => SetField(ref resourcePrototypeSummaryText, value);
    }

    public string LogFilter
    {
        get => logFilter;
        set => SetField(ref logFilter, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set => SetField(ref isBusy, value);
    }

    public bool IsResourcePrototypeScanning
    {
        get => isResourcePrototypeScanning;
        private set => SetField(ref isResourcePrototypeScanning, value);
    }

    public bool IsThanosBusy
    {
        get => isThanosBusy;
        private set => SetField(ref isThanosBusy, value);
    }

    public MigrationMode CurrentMode
    {
        get => currentMode;
        set
        {
            if (SetField(ref currentMode, value))
            {
                OnPropertyChanged(nameof(IsThanosMode));
                OnPropertyChanged(nameof(CurrentModeIndex));
                RefreshSelectedJobDetails();
            }
        }
    }

    public int CurrentModeIndex
    {
        get => (int)CurrentMode;
        set
        {
            if (value < 0)
                value = 0;
            if (value > 1)
                value = 1;
            CurrentMode = (MigrationMode)value;
        }
    }

    public bool IsThanosMode => CurrentMode == MigrationMode.ThanosRaid;

    public ThanosMigrationReport? ThanosReport
    {
        get => thanosReport;
        private set => SetField(ref thanosReport, value);
    }

    public ObservableCollection<ThanosMigrationStep> ThanosSteps { get; } = [];

    public UpkMigrationViewModel(
        UpkMigrationService service,
        ThanosStructuralMigrationService thanosStructuralService,
        ThanosTextureMigrationService thanosTextureService,
        ThanosTextureCacheInjectorService thanosTextureCacheInjectorService,
        TfcManifestService tfcManifestService,
        DispatcherQueue? dispatcherQueue = null)
    {
        this.dispatcherQueue = dispatcherQueue;
        this.service = service;
        this.thanosStructuralService = thanosStructuralService;
        this.thanosTextureService = thanosTextureService;
        this.thanosTextureCacheInjectorService = thanosTextureCacheInjectorService;
        this.tfcManifestService = tfcManifestService;
        config = UpkMigrationConfigStore.Load();
        if (string.IsNullOrWhiteSpace(config.LogPath))
            config.LogPath = RuntimeLogPaths.UpkMigrationResourceScannerLogPath;

        if (!string.IsNullOrWhiteSpace(config.OutputRoot))
            outputDirectory = config.OutputRoot;
        if (!string.IsNullOrWhiteSpace(config.ResourcePrototypeSourcePath))
            resourcePrototypeSourcePath = config.ResourcePrototypeSourcePath;
        else if (!string.IsNullOrWhiteSpace(config.SipPath152))
            resourcePrototypeSourcePath = config.SipPath152;
        if (!string.IsNullOrWhiteSpace(config.ResourcePrototypeOutputRoot))
            resourcePrototypeOutputDirectory = config.ResourcePrototypeOutputRoot;
        if (!string.IsNullOrWhiteSpace(config.ThanosTextureSourceRoot148))
            textureSourceDirectory148 = config.ThanosTextureSourceRoot148;
        else if (!string.IsNullOrWhiteSpace(config.GameRoot148))
            textureSourceDirectory148 = config.GameRoot148;

        prototypeMerger = new ThanosPrototypeMergerViewModel(
            new ThanosPrototypeDiscoveryService(),
            new ThanosPrototypeMergePlanner(),
            new ThanosPrototypeMergerService());
        service.LogEntryAdded += Service_LogEntryAdded;
        service.ProgressChanged += Service_ProgressChanged;
        service.StatusChanged += Service_StatusChanged;
        service.JobUpdated += Service_JobUpdated;

        SelectUpksCommand = new AsyncRelayCommand(SelectUpksAsync);
        StartMigrationCommand = new AsyncRelayCommand(StartMigrationAsync);
        BrowseOutputDirectoryCommand = new AsyncRelayCommand(BrowseOutputDirectoryAsync);
        BrowseTextureManifestDirectoryCommand = new AsyncRelayCommand(BrowseTextureManifestDirectoryAsync);
        BrowseTextureSourceDirectoryCommand = new AsyncRelayCommand(BrowseTextureSourceDirectoryAsync);
        BrowseResourcePrototypeSourceCommand = new AsyncRelayCommand(BrowseResourcePrototypeSourceAsync);
        BrowseResourcePrototypeOutputCommand = new AsyncRelayCommand(BrowseResourcePrototypeOutputAsync);
        ScanResourcePrototypesCommand = new AsyncRelayCommand(ScanResourcePrototypesAsync);
        AnalyzeThanosCommand = new AsyncRelayCommand(AnalyzeThanosAsync);
        MigrateThanosCommand = new AsyncRelayCommand(MigrateThanosAsync);
        UpdateTextureManifestCommand = new AsyncRelayCommand(UpdateTextureManifestAsync);
        InjectTextureCachePayloadsCommand = new AsyncRelayCommand(InjectTextureCachePayloadsAsync);
        RunKnowhereProfileCommand = new AsyncRelayCommand(RunKnowhereProfileAsync);
        RollbackTextureCacheCommand = new AsyncRelayCommand(RollbackTextureCacheAsync);

        InitializeToolingState();
        UpdateStatus("Ready.");
        ResourcePrototypeSummaryText = "No resource prototype scan has run yet.";
    }

    public async Task SelectUpksAsync()
    {
        IReadOnlyList<string> paths = SelectUpksRequestedAsync is null ? [] : await SelectUpksRequestedAsync();
        if (paths.Count == 0)
        {
            UpdateStatus("No UPKs were selected.");
            return;
        }

        Jobs.Clear();
        foreach (string path in paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            MigrationJob job = new()
            {
                SourceUpkPath = path,
                OutputUpkPath = BuildOutputPath(path),
                Status = MigrationJobStatus.Pending,
                CurrentStep = "Pending"
            };
            Jobs.Add(job);
        }

        RefreshJobOutputPaths();
        UpdateStatus($"{Jobs.Count} UPK file(s) queued for migration.");
        RefreshSelectedJobDetails();
        RefreshSummary();
    }

    public async Task StartMigrationAsync()
    {
        if (IsBusy)
            return;

        if (Jobs.Count == 0)
        {
            UpdateStatus("Select one or more 1.48 UPKs before starting migration.");
            return;
        }

        if (!TryEnsureOutputDirectory(out string? validatedOutputDirectory) || string.IsNullOrWhiteSpace(validatedOutputDirectory))
        {
            UpdateStatus("Choose a valid output directory before starting migration.");
            return;
        }

        IsBusy = true;
        try
        {
            foreach (MigrationJob job in Jobs)
                job.IsThanosRaid = IsThanosMode;

            ProgressText = "Starting migration pipeline...";
            try
            {
                await service.RunMigrationAsync(Jobs, validatedOutputDirectory, TextureManifestDirectory);
                if (IsThanosMode && !string.IsNullOrWhiteSpace(TextureManifestDirectory))
                {
                    UpdateStatus("Migration complete. Updating texture manifest...");
                    await UpdateTextureManifestAsync().ConfigureAwait(true);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Migration failed: {ex}");
                ResultsSummaryText = ex.ToString();
            }
            StatusText = service.StatusText;
            ProgressText = service.ProgressText;
            OverallProgress = service.OverallProgress;
            ResultsSummaryText = string.IsNullOrWhiteSpace(ResultsSummaryText) ? service.LastRunSummaryText : ResultsSummaryText;
            RefreshSelectedJobDetails();
        }
        finally
        {
            IsBusy = false;
            RefreshSummary();
        }
    }

    public async Task BrowseOutputDirectoryAsync()
    {
        if (BrowseOutputDirectoryRequestedAsync is null)
            return;

        string? folder = await BrowseOutputDirectoryRequestedAsync();
        if (!string.IsNullOrWhiteSpace(folder))
            OutputDirectory = folder;
    }

    public async Task BrowseTextureManifestDirectoryAsync()
    {
        if (BrowseTextureManifestDirectoryRequestedAsync is null)
            return;

        string? folder = await BrowseTextureManifestDirectoryRequestedAsync();
        if (!string.IsNullOrWhiteSpace(folder))
            TextureManifestDirectory = folder;
    }

    public async Task BrowseTextureSourceDirectoryAsync()
    {
        if (BrowseTextureSourceDirectoryRequestedAsync is null)
            return;

        string? folder = await BrowseTextureSourceDirectoryRequestedAsync();
        if (!string.IsNullOrWhiteSpace(folder))
            TextureSourceDirectory148 = folder;
    }

    public async Task AnalyzeThanosAsync()
    {
        if (IsBusy || IsThanosBusy)
            return;

        MigrationJob[] jobsToAnalyze = Jobs.Where(job => !string.IsNullOrWhiteSpace(job.SourceUpkPath)).ToArray();
        if (jobsToAnalyze.Length == 0)
        {
            UpdateStatus("Select a UPK before analyzing Thanos Raid mode.");
            return;
        }

        IsThanosBusy = true;
        try
        {
            await InvokeOnUiAsync(() => CurrentMode = MigrationMode.ThanosRaid).ConfigureAwait(false);
            PostToUi(() =>
            {
                ThanosSteps.Clear();
                ThanosReport = null;
                ThanosSteps.Add(new ThanosMigrationStep
                {
                    Name = "Structural Analysis",
                    Description = "Inspect header, tables, and export layout.",
                    Status = ThanosMigrationStepStatus.Pending
                });
                ThanosSteps.Add(new ThanosMigrationStep
                {
                    Name = "Texture Analysis",
                    Description = "Inspect Texture2D exports and streaming references.",
                    Status = ThanosMigrationStepStatus.Pending
                });
            });

            ThanosMigrationReport combinedReport = new()
            {
                FilePath = "Batch Thanos Analysis"
            };
            List<ThanosMigrationReport?> batchReports = [];
            for (int i = 0; i < jobsToAnalyze.Length; i++)
            {
                MigrationJob job = jobsToAnalyze[i];
                await InvokeOnUiAsync(() => SelectedJob = job).ConfigureAwait(false);
                ThanosMigrationReport? report = await AnalyzeThanosJobAsync(job, i + 1, jobsToAnalyze.Length).ConfigureAwait(false);
                batchReports.Add(report);
                if (report is not null)
                {
                    job.ThanosReport = report;
                    job.AnalyzeMeshCount = report.SkeletalMeshCount + report.StaticMeshCount;
                    job.AnalyzeTextureCount = report.TextureCount;
                    job.AnalyzeAnimationCount = report.AnimationCount;
                    job.AnalyzeMaterialCount = report.MaterialCount;
                    combinedReport.Findings.AddRange(report.Findings);
                    combinedReport.SkeletalMeshCount += report.SkeletalMeshCount;
                    combinedReport.StaticMeshCount += report.StaticMeshCount;
                    combinedReport.TextureCount += report.TextureCount;
                    combinedReport.AnimationCount += report.AnimationCount;
                    combinedReport.MaterialCount += report.MaterialCount;
                }
            }

            PostToUi(() =>
            {
                ThanosReport = batchReports.LastOrDefault(report => report is not null) ?? combinedReport;

                if (ThanosSteps.Count >= 2)
                {
                    ThanosSteps[0] = new ThanosMigrationStep
                    {
                        Name = "Structural Analysis",
                        Description = "Inspect header, tables, and export layout.",
                        Status = ThanosMigrationStepStatus.Done,
                        Reason = $"{combinedReport.Findings.Count} finding(s)"
                    };
                    ThanosSteps[1] = new ThanosMigrationStep
                    {
                        Name = "Texture Analysis",
                        Description = "Inspect Texture2D exports and streaming references.",
                        Status = ThanosMigrationStepStatus.Done,
                        Reason = $"{combinedReport.Findings.Count} finding(s)"
                    };
                }
            });

            await InvokeOnUiAsync(() => UpdateStatus("Thanos Raid analysis complete.")).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await InvokeOnUiAsync(() =>
            {
                ThanosSteps.Clear();
                ThanosSteps.Add(new ThanosMigrationStep
                {
                    Name = "Thanos Analysis",
                    Description = "Analyze Thanos Raid package state.",
                    Status = ThanosMigrationStepStatus.Failed,
                    Reason = ex.Message,
                    Exception = ex
                });

                UpdateStatus($"Thanos analysis failed: {ex.Message}");
            }).ConfigureAwait(false);
        }
        finally
        {
            await InvokeOnUiAsync(() => IsThanosBusy = false).ConfigureAwait(false);
        }
    }

    public async Task MigrateThanosAsync()
    {
        CurrentMode = MigrationMode.ThanosRaid;
        foreach (MigrationJob job in Jobs)
            job.IsThanosRaid = true;

        await AnalyzeThanosAsync();
        await StartMigrationAsync();

        if (!string.IsNullOrWhiteSpace(OutputDirectory))
        {
            string manifestPath = Path.Combine(Path.GetFullPath(OutputDirectory), TextureManifest.ManifestName);
            if (File.Exists(manifestPath))
            {
                int manifestCount = tfcManifestService.LoadManifest(manifestPath).Count;
                UpdateStatus($"Thanos TFC manifest loaded: {manifestCount} entry(s).");
            }
        }
    }

    public Task UpdateTextureManifestAsync()
    {
        if (IsBusy || IsThanosBusy)
            return Task.CompletedTask;

        if (!IsThanosMode)
            CurrentMode = MigrationMode.ThanosRaid;

        if (string.IsNullOrWhiteSpace(TextureManifestDirectory))
        {
            UpdateStatus("Select the 1.52 client folder that contains TextureFileCacheManifest.bin.");
            return Task.CompletedTask;
        }

        string sourceManifestPath = string.Empty;
        if (!string.IsNullOrWhiteSpace(OutputDirectory))
            sourceManifestPath = Path.Combine(Path.GetFullPath(OutputDirectory), TextureManifest.ManifestName);

        bool hasOutputManifest = !string.IsNullOrWhiteSpace(sourceManifestPath) && File.Exists(sourceManifestPath);
        if (!hasOutputManifest && !string.IsNullOrWhiteSpace(TextureSourceDirectory148))
        {
            string sourceRoot148 = Path.GetFullPath(TextureSourceDirectory148);
            string fallbackManifestPath = Path.Combine(sourceRoot148, TextureManifest.ManifestName);
            if (File.Exists(fallbackManifestPath))
                sourceManifestPath = fallbackManifestPath;
        }

        if (!File.Exists(sourceManifestPath))
        {
            UpdateStatus("No source texture manifest found. Set either the migration output folder or the 1.48 texture source folder.");
            return Task.CompletedTask;
        }

        string targetManifestPath = Path.Combine(Path.GetFullPath(TextureManifestDirectory), TextureManifest.ManifestName);
        if (!File.Exists(targetManifestPath))
        {
            UpdateStatus("Select the 1.52 client folder that already contains TextureFileCacheManifest.bin.");
            return Task.CompletedTask;
        }

        try
        {
            List<ThanosTfcEntry> sourceEntries = tfcManifestService.LoadManifest(sourceManifestPath);
            if (sourceEntries.Count == 0)
            {
                UpdateStatus("The migrated manifest did not contain any texture entries to inject.");
                return Task.CompletedTask;
            }

            List<ThanosTfcEntry> existingEntries = tfcManifestService.LoadManifest(targetManifestPath);
            List<ThanosTfcEntry> mergedEntries = existingEntries.Count > 0
                ? tfcManifestService.MergeEntries(existingEntries, sourceEntries)
                : sourceEntries;

            string backupManifestPath = Path.Combine(
                Path.GetDirectoryName(targetManifestPath) ?? TextureManifestDirectory!,
                $"TextureFileCacheManifest_{DateTime.Now:yyyyMMdd_HHmmss}.bin.bak");
            File.Copy(targetManifestPath, backupManifestPath, overwrite: true);

            tfcManifestService.SaveManifest(targetManifestPath, mergedEntries);
            UpdateStatus($"Texture manifest updated: {targetManifestPath} ({mergedEntries.Count:N0} entry(s)). Backup: {backupManifestPath}");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Texture manifest update failed: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public async Task InjectTextureCachePayloadsAsync()
    {
        const string injectionStepName = "TFC Injection";
        const string injectionStepDescription = "Run Knowhere profile: preflight, inject, verify, and transactional swap.";

        if (IsBusy || IsThanosBusy)
        {
            UpdateStatus("Injection is blocked while another migration task is running.");
            TextureInjectProgressText = "Injection progress: 0% - Blocked (tool is busy)";
            return;
        }

        if (!IsThanosMode)
            CurrentMode = MigrationMode.ThanosRaid;

        if (string.IsNullOrWhiteSpace(TextureSourceDirectory148))
        {
            UpdateStatus("Select the 1.48 source folder that contains CharTextures.tfc, Textures.tfc, and TextureFileCacheManifest.bin.");
            TextureInjectProgressText = "Injection progress: 0% - Missing 1.48 source folder";
            return;
        }

        if (string.IsNullOrWhiteSpace(TextureManifestDirectory))
        {
            UpdateStatus("Select the 1.52 client folder that contains CharTextures.tfc, Textures.tfc, and TextureFileCacheManifest.bin.");
            TextureInjectProgressText = "Injection progress: 0% - Missing 1.52 target folder";
            return;
        }

        IsThanosBusy = true;
        try
        {
            await InvokeOnUiAsync(() =>
            {
                ThanosSteps.Clear();
                ThanosSteps.Add(new ThanosMigrationStep
                {
                    Name = injectionStepName,
                    Description = injectionStepDescription,
                    Status = ThanosMigrationStepStatus.Pending,
                    Reason = "Starting..."
                });
                TextureInjectProgress = 0;
                TextureInjectProgressText = "Injection progress: 0% - Starting";
                UpdateStatus("Starting TFC payload injection...");
            }).ConfigureAwait(false);

            string sourceRoot = ResolveCookedPcConsoleRoot(TextureSourceDirectory148);
            string targetRoot = ResolveCookedPcConsoleRoot(TextureManifestDirectory);
            string sourceManifestPath = Path.Combine(sourceRoot, "TextureFileCacheManifest.bin");
            string targetManifestPath = Path.Combine(targetRoot, "TextureFileCacheManifest.bin");
            AddServiceLog("Info", $"Resolved 1.48 texture source folder: {sourceRoot}");
            AddServiceLog("Info", $"Resolved 1.52 texture target folder: {targetRoot}");
            if (!File.Exists(sourceManifestPath))
            {
                UpdateStatus($"Source manifest missing: {sourceManifestPath}");
                TextureInjectProgressText = "Injection progress: 0% - Source manifest missing";
                if (ThanosSteps.Count > 0)
                {
                    ThanosSteps[0] = new ThanosMigrationStep
                    {
                        Name = injectionStepName,
                        Description = injectionStepDescription,
                        Status = ThanosMigrationStepStatus.Failed,
                        Reason = $"Source manifest missing: {sourceManifestPath}"
                    };
                }
                return;
            }

            if (!File.Exists(targetManifestPath))
            {
                UpdateStatus($"Target manifest missing: {targetManifestPath}");
                TextureInjectProgressText = "Injection progress: 0% - Target manifest missing";
                if (ThanosSteps.Count > 0)
                {
                    ThanosSteps[0] = new ThanosMigrationStep
                    {
                        Name = injectionStepName,
                        Description = injectionStepDescription,
                        Status = ThanosMigrationStepStatus.Failed,
                        Reason = $"Target manifest missing: {targetManifestPath}"
                    };
                }
                return;
            }

            ThanosTextureCacheInjectionResult result = await Task.Run(() =>
                thanosTextureCacheInjectorService.InjectMissingEntries(
                    sourceRoot,
                    targetRoot,
                    (percent, message) => PostToUi(() =>
                    {
                        double clamped = Math.Clamp(percent, 0, 100);
                        TextureInjectProgress = clamped;
                        TextureInjectProgressText = $"Injection progress: {clamped:0}% - {message}";
                        if (ThanosSteps.Count > 0)
                        {
                            ThanosSteps[0] = new ThanosMigrationStep
                            {
                                Name = injectionStepName,
                                Description = injectionStepDescription,
                                Status = clamped >= 100 ? ThanosMigrationStepStatus.Done : ThanosMigrationStepStatus.Pending,
                                Reason = message
                            };
                        }
                    }),
                    message => PostToUi(() => UpdateStatus(message)))).ConfigureAwait(true);

            if (result.Warnings.Count > 0)
            {
                foreach (string warning in result.Warnings)
                    AddServiceLog("Warning", warning);
            }

            foreach (string backup in result.BackupPaths)
                AddServiceLog("Info", $"Backup: {backup}");

            foreach (string tfcPath in result.TouchedTargetTfcFiles)
                AddServiceLog("Info", $"Injected target cache: {tfcPath}");

            UpdateStatus(
                $"Knowhere profile complete. Mode={result.SourceCandidateMode}, DependencyUpks={result.DependencySourceUpkCount:N0}, SignalTextures={result.DependencyTextureSignalCount:N0}, " +
                $"Preflight={result.PreflightPassed}, SourceChunks={result.PreflightSourceChunks:N0}, " +
                $"TargetChunks={result.PreflightTargetChunks:N0}, Matched={result.SourceMatchedEntries:N0}, AddedTextures={result.AddedTextureEntries:N0}, " +
                $"PatchedTextures={result.PatchedTextureEntries:N0}, ReplacedInvalid={result.ReplacedInvalidChunks:N0}, AddedChunks={result.AddedChunks:N0}, " +
                $"Verified={result.VerifiedChunks:N0}, VerifyFailures={result.VerificationFailures:N0}, " +
                $"ManifestEntries={result.TargetEntriesBefore:N0}->{result.TargetEntriesAfter:N0}.");
            TextureInjectProgress = 100;
            TextureInjectProgressText = "Injection progress: 100% - Complete";
            if (ThanosSteps.Count > 0)
            {
                ThanosSteps[0] = new ThanosMigrationStep
                {
                    Name = injectionStepName,
                    Description = injectionStepDescription,
                    Status = ThanosMigrationStepStatus.Done,
                    Reason = "Complete."
                };
            }
        }
        catch (Exception ex)
        {
            string detail = ex.Message;
            if (ex.InnerException is not null && !string.IsNullOrWhiteSpace(ex.InnerException.Message))
                detail = $"{detail} | Inner: {ex.InnerException.Message}";

            AddServiceLog("Error", ex.ToString());
            UpdateStatus($"TFC inject failed: {detail}");
            TextureInjectProgressText = $"Injection progress: {TextureInjectProgress:0}% - Failed: {detail}";
            if (ThanosSteps.Count > 0)
            {
                ThanosSteps[0] = new ThanosMigrationStep
                {
                    Name = injectionStepName,
                    Description = injectionStepDescription,
                    Status = ThanosMigrationStepStatus.Failed,
                    Reason = detail,
                    Exception = ex
                };
            }
        }
        finally
        {
            IsThanosBusy = false;
        }
    }

    public Task RunKnowhereProfileAsync()
        => InjectTextureCachePayloadsAsync();

    public async Task RollbackTextureCacheAsync()
    {
        if (IsBusy || IsThanosBusy)
        {
            UpdateStatus("Rollback is blocked while another migration task is running.");
            return;
        }

        if (string.IsNullOrWhiteSpace(TextureManifestDirectory))
        {
            UpdateStatus("Select the 1.52 client folder before running rollback.");
            return;
        }

        IsThanosBusy = true;
        try
        {
            string targetRoot = ResolveCookedPcConsoleRoot(TextureManifestDirectory);
            AddServiceLog("Info", $"Rollback target folder: {targetRoot}");

            ThanosTextureCacheRollbackResult result = await Task.Run(() =>
                thanosTextureCacheInjectorService.RollbackLatestBackup(targetRoot)).ConfigureAwait(true);

            if (!result.Restored)
            {
                UpdateStatus("Rollback skipped: no matching backup set found.");
                return;
            }

            foreach (string restoredPath in result.RestoredPaths)
                AddServiceLog("Info", $"Restored: {restoredPath}");

            foreach (string missingPath in result.MissingBackupPaths)
                AddServiceLog("Warning", $"Missing backup: {missingPath}");

            UpdateStatus($"Rollback complete. Timestamp={result.Timestamp}, Restored={result.RestoredPaths.Count:N0} file(s).");
        }
        catch (Exception ex)
        {
            AddServiceLog("Error", ex.ToString());
            UpdateStatus($"Rollback failed: {ex.Message}");
        }
        finally
        {
            IsThanosBusy = false;
        }
    }

    public async Task ScanResourcePrototypesAsync()
    {
        if (IsBusy || IsResourcePrototypeScanning)
            return;

        if (string.IsNullOrWhiteSpace(ResourcePrototypeSourcePath))
        {
            UpdateResourcePrototypeStatus("Select a resource prototype source path before scanning.");
            return;
        }

        IsResourcePrototypeScanning = true;
        try
        {
            UpkMigrationConfigStore.Save(config);
            ResourcePrototypeScanReport report = await resourcePrototypeScanner.ScanAsync(
                ResourcePrototypeSourcePath,
                config,
                message => PostToUi(() => ResourcePrototypeStatusText = message)).ConfigureAwait(false);

            PostToUi(() =>
            {
                ClientMapDependencies.Clear();
                foreach (ClientMapDependency dependency in report.ClientMapDependencies)
                    ClientMapDependencies.Add(dependency);

                ResourcePrototypeSummaryText = report.Summary;
                ResourcePrototypeStatusText = $"Scanned {report.ClientMapCount:N0} ClientMap dependency set(s).";
            });

            string outputDirectory = string.IsNullOrWhiteSpace(ResourcePrototypeOutputDirectory)
                ? OutputDirectory
                : ResourcePrototypeOutputDirectory;
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                string writtenDirectory = await resourcePrototypeScanner.ExportAsync(report, outputDirectory, config.LogPath).ConfigureAwait(false);
                PostToUi(() => ResourcePrototypeStatusText = $"Resource prototype scan exported to {writtenDirectory}.");
            }
        }
        catch (Exception ex)
        {
            UpdateResourcePrototypeStatus($"Resource prototype scan failed: {ex.Message}");
        }
        finally
        {
            IsResourcePrototypeScanning = false;
        }
    }

    private async Task<ThanosMigrationReport?> AnalyzeThanosJobAsync(MigrationJob job, int jobIndex, int totalJobs)
    {
        job.IsThanosRaid = true;
        await InvokeOnUiAsync(() =>
        {
            job.AnalyzeProgress = 0;
            job.CurrentStep = "Analyzing...";
        }).ConfigureAwait(false);

        try
        {
            await InvokeOnUiAsync(() =>
            {
                job.Status = MigrationJobStatus.Running;
                job.AnalyzeProgress = 0;
                job.CurrentStep = "Analyzing Thanos package...";
            }).ConfigureAwait(false);
            ThanosMigrationReport structuralReport = await thanosStructuralService.Analyze(
                job.SourceUpkPath,
                (progress, message) => PostToUi(() =>
                {
                    job.AnalyzeProgress = progress * 0.5;
                    job.CurrentStep = message;
                    ProgressText = $"Analyzing {jobIndex} of {totalJobs} UPK file(s)...";
                    StatusText = message;
                })).ConfigureAwait(false);

            IReadOnlyList<ThanosMigrationFinding> textureFindings = await thanosTextureService.AnalyzeTextures(
                job.SourceUpkPath,
                (progress, message) => PostToUi(() =>
                {
                    job.AnalyzeProgress = 50 + (progress * 0.5);
                    job.CurrentStep = message;
                    ProgressText = $"Analyzing {jobIndex} of {totalJobs} UPK file(s)...";
                    StatusText = message;
                })).ConfigureAwait(false);

            structuralReport.Findings.AddRange(textureFindings);
            structuralReport.FilePath = job.SourceUpkPath;
            await InvokeOnUiAsync(() =>
            {
                job.AnalyzeMeshCount = structuralReport.SkeletalMeshCount + structuralReport.StaticMeshCount;
                job.AnalyzeTextureCount = structuralReport.TextureCount;
                job.AnalyzeAnimationCount = structuralReport.AnimationCount;
                job.AnalyzeMaterialCount = structuralReport.MaterialCount;
                job.ThanosReport = structuralReport;
                job.Status = MigrationJobStatus.Completed;
                job.AnalyzeProgress = 100;
                job.CurrentStep = "Thanos analysis complete.";
            }).ConfigureAwait(false);

            return structuralReport;
        }
        catch (Exception ex)
        {
            ThanosMigrationReport failedReport = new()
            {
                FilePath = job.SourceUpkPath
            };
            await InvokeOnUiAsync(() =>
            {
                job.Status = MigrationJobStatus.Failed;
                job.ThanosReport = failedReport;
                job.AnalyzeMeshCount = 0;
                job.AnalyzeTextureCount = 0;
                job.AnalyzeAnimationCount = 0;
                job.AnalyzeMaterialCount = 0;
                job.AnalyzeProgress = 100;
                job.CurrentStep = $"Thanos analysis failed: {ex.Message}";
            }).ConfigureAwait(false);
            return failedReport;
        }
    }

    private Task InvokeOnUiAsync(Action action)
    {
        if (dispatcherQueue is null || dispatcherQueue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                action();
                completion.TrySetResult(true);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        return completion.Task;
    }

    private void Service_LogEntryAdded(MigrationLogEntry entry)
    {
        PostToUi(() =>
        {
            LogEntries.Add(entry);
            ResultsSummaryText = service.LastRunSummaryText;
        });
    }

    private void Service_ProgressChanged(double progress, string message)
    {
        PostToUi(() =>
        {
            OverallProgress = progress;
            ProgressText = message;
            StatusText = message;
        });
    }

    private void Service_StatusChanged(string message)
    {
        PostToUi(() => StatusText = message);
    }

    private void Service_JobUpdated(MigrationJob job)
    {
        PostToUi(() =>
        {
            if (ReferenceEquals(SelectedJob, job))
                RefreshSelectedJobDetails();

            RefreshSummary();
        });
    }

    private void SelectedJob_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshSelectedJobDetails();
        RefreshThanosReport();
        RefreshSummary();
    }

    private void RefreshSelectedJobDetails()
    {
        SelectedJobDetails = SelectedJob?.DetailsText ?? "No job selected.";
    }

    private void RefreshThanosReport()
    {
        ThanosReport = SelectedJob?.ThanosReport;
    }

    private void RefreshSummary()
    {
        ResultsSummaryText = service.LastRunSummaryText;
    }

    private void RefreshJobOutputPaths()
    {
        foreach (MigrationJob job in Jobs)
            job.OutputUpkPath = BuildOutputPath(job.SourceUpkPath);
    }

    private bool TryEnsureOutputDirectory(out string? validatedOutputDirectory)
    {
        validatedOutputDirectory = null;
        if (string.IsNullOrWhiteSpace(OutputDirectory))
            return false;

        try
        {
            validatedOutputDirectory = Path.GetFullPath(OutputDirectory);
            Directory.CreateDirectory(validatedOutputDirectory);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string BuildOutputPath(string sourcePath)
    {
        if (!TryEnsureOutputDirectory(out string? validatedOutputDirectory) || string.IsNullOrWhiteSpace(validatedOutputDirectory))
            return string.Empty;

        string fileName = Path.GetFileNameWithoutExtension(sourcePath);
        return Path.Combine(validatedOutputDirectory, $"{fileName}_152.upk");
    }

    private void UpdateStatus(string message)
    {
        StatusText = message;
        service.StatusText = message;
    }

    private async Task BrowseResourcePrototypeSourceAsync()
    {
        if (BrowseResourcePrototypeSourceRequestedAsync is null)
            return;

        string? path = await BrowseResourcePrototypeSourceRequestedAsync().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path))
            ResourcePrototypeSourcePath = path;
    }

    private async Task BrowseResourcePrototypeOutputAsync()
    {
        if (BrowseResourcePrototypeOutputRequestedAsync is null)
            return;

        string? path = await BrowseResourcePrototypeOutputRequestedAsync().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path))
            ResourcePrototypeOutputDirectory = path;
    }

    private void UpdateResourcePrototypeStatus(string message)
    {
        ResourcePrototypeStatusText = message;
    }

    private void AddServiceLog(string level, string message)
    {
        MigrationLogEntry entry = new()
        {
            Timestamp = DateTime.Now.ToString("HH:mm:ss"),
            Level = level,
            Message = message
        };
        LogEntries.Add(entry);
    }

    private static string ResolveCookedPcConsoleRoot(string selectedPath)
    {
        string full = Path.GetFullPath(selectedPath);
        string manifest = Path.Combine(full, "TextureFileCacheManifest.bin");
        if (File.Exists(manifest))
            return full;

        string candidate = Path.Combine(full, "UnrealEngine3", "MarvelGame", "CookedPCConsole");
        if (File.Exists(Path.Combine(candidate, "TextureFileCacheManifest.bin")))
            return candidate;

        return full;
    }

    private void PostToUi(Action action)
    {
        if (dispatcherQueue is null || dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        dispatcherQueue.TryEnqueue(() => action());
    }

    private bool SetField<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
            return false;

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task>? executeAsync;
        private readonly Func<object?, Task>? execute;

        public AsyncRelayCommand(Func<Task> execute)
        {
            executeAsync = execute;
        }

        public AsyncRelayCommand(Func<object?, Task> execute)
        {
            this.execute = execute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public async void Execute(object? parameter)
        {
            try
            {
                if (executeAsync is not null)
                {
                    await executeAsync();
                    return;
                }

                if (execute is not null)
                    await execute(parameter);
            }
            catch (Exception)
            {
            }
        }
    }
}

