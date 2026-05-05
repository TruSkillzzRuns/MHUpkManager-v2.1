using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using OmegaAssetStudio.ThanosMigration.Models;
using OmegaAssetStudio.ThanosMigration.Services;
using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration;

public sealed class ThanosPrototypeMergerViewModel : INotifyPropertyChanged
{
    private const int MaxVisiblePrototypeRows = 200;
    private readonly ThanosPrototypeDiscoveryService discoveryService;
    private readonly ThanosPrototypeMergePlanner mergePlanner;
    private readonly ThanosPrototypeMergerService mergerService;
    private readonly ThanosDependencyScannerService dependencyScannerService;
    private readonly UpkFileRepository upkRepository = new();
    private ThanosDependencyReport? selectedReport;
    private string? client148Root;
    private string? client152Root;
    private string? sourceUpkPath;
    private readonly List<string> selectedSourceUpkPaths = [];
    private string sourceUpkSelectionText = "No source UPKs selected.";
    private bool isMerging;
    private bool isDiscovering;
    private bool showOnlyRaidRelevant = true;
    private string statusText = "Ready.";
    private double discoveryProgressValue;
    private double discoveryProgressMaximum = 100.0;
    private string discoveryCurrentFile = string.Empty;
    private string discoveryStatus = string.Empty;
    private int hiddenDiscoveryCount;
    private IReadOnlyList<ThanosPrototypeSource> lastDiscoveryResults = [];
    private readonly AsyncRelayCommand loadReportCommand;
    private readonly AsyncRelayCommand discoverPrototypesCommand;
    private readonly AsyncRelayCommand buildMergePlansCommand;
    private readonly AsyncRelayCommand runMergeCommand;
    private readonly AsyncRelayCommand runAutoPipelineCommand;
    private readonly AsyncRelayCommand browseSourceUpkCommand;
    private readonly AsyncRelayCommand browseClient148RootCommand;
    private readonly AsyncRelayCommand browseClient152RootCommand;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Func<Task<string?>>? BrowseReportRequestedAsync { get; set; }

    public Func<Task<string?>>? BrowseClient148RootRequestedAsync { get; set; }

    public Func<Task<string?>>? BrowseClient152RootRequestedAsync { get; set; }

    public Func<Task<IReadOnlyList<string>>>? BrowseSourceUpksRequestedAsync { get; set; }

    public IAsyncRelayCommand LoadReportCommand => loadReportCommand;

    public IAsyncRelayCommand DiscoverPrototypesCommand => discoverPrototypesCommand;

    public IAsyncRelayCommand BuildMergePlansCommand => buildMergePlansCommand;

    public IAsyncRelayCommand RunMergeCommand => runMergeCommand;

    public IAsyncRelayCommand RunAutoPipelineCommand => runAutoPipelineCommand;

    public IAsyncRelayCommand BrowseSourceUpkCommand => browseSourceUpkCommand;

    public IAsyncRelayCommand BrowseClient148RootCommand => browseClient148RootCommand;

    public IAsyncRelayCommand BrowseClient152RootCommand => browseClient152RootCommand;

    public ObservableCollection<ThanosPrototypeSource> DiscoveredPrototypes { get; } = [];

    public ObservableCollection<ThanosPrototypeMergePlan> MergePlans { get; } = [];

    public ObservableCollection<ThanosMigrationStep> MergeSteps { get; } = [];

    public ThanosDependencyReport? SelectedReport
    {
        get => selectedReport;
        set
        {
            if (SetField(ref selectedReport, value))
            {
                MergePlans.Clear();
                DiscoveredPrototypes.Clear();
                MergeSteps.Clear();
                StatusText = value is null ? "Ready." : $"Loaded report: {Path.GetFileName(value.FilePath)}";
                RefreshCommandStates();
            }
        }
    }

    public string? Client148Root
    {
        get => client148Root;
        set
        {
            if (SetField(ref client148Root, value))
            {
                PrototypeMergerSessionStore.Remember(client148Root: client148Root);
                RefreshCommandStates();
            }
        }
    }

    public string? Client152Root
    {
        get => client152Root;
        set
        {
            if (SetField(ref client152Root, value))
            {
                PrototypeMergerSessionStore.Remember(client152Root: client152Root);
                RefreshCommandStates();
            }
        }
    }

    public string? SourceUpkPath
    {
        get => sourceUpkPath;
        set
        {
            if (SetField(ref sourceUpkPath, value))
            {
                SyncSourceUpkSelectionFromText(value);
                PrototypeMergerSessionStore.Remember(sourceUpkPath: sourceUpkPath);
                RefreshCommandStates();
            }
        }
    }

    public string SourceUpkSelectionText
    {
        get => sourceUpkSelectionText;
        private set => SetField(ref sourceUpkSelectionText, value);
    }

    public bool IsMerging
    {
        get => isMerging;
        private set
        {
            if (SetField(ref isMerging, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                RefreshCommandStates();
            }
        }
    }

    public bool IsDiscovering
    {
        get => isDiscovering;
        private set
        {
            if (SetField(ref isDiscovering, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                RefreshCommandStates();
            }
        }
    }

    public bool IsBusy => IsMerging || IsDiscovering;

    public double DiscoveryProgressValue
    {
        get => discoveryProgressValue;
        private set => SetField(ref discoveryProgressValue, value);
    }

    public double DiscoveryProgressMaximum
    {
        get => discoveryProgressMaximum;
        private set => SetField(ref discoveryProgressMaximum, value);
    }

    public string DiscoveryCurrentFile
    {
        get => discoveryCurrentFile;
        private set => SetField(ref discoveryCurrentFile, value);
    }

    public string DiscoveryStatus
    {
        get => discoveryStatus;
        private set => SetField(ref discoveryStatus, value);
    }

    public int HiddenDiscoveryCount
    {
        get => hiddenDiscoveryCount;
        private set => SetField(ref hiddenDiscoveryCount, value);
    }

    public bool ShowOnlyRaidRelevant
    {
        get => showOnlyRaidRelevant;
        set
        {
            if (SetField(ref showOnlyRaidRelevant, value))
                ApplyDiscoveryFilter();
        }
    }

    public string StatusText
    {
        get => statusText;
        private set => SetField(ref statusText, value);
    }

    public ThanosPrototypeMergerViewModel(
        ThanosPrototypeDiscoveryService discoveryService,
        ThanosPrototypeMergePlanner mergePlanner,
        ThanosPrototypeMergerService mergerService)
    {
        this.discoveryService = discoveryService;
        this.mergePlanner = mergePlanner;
        this.mergerService = mergerService;
        dependencyScannerService = new ThanosDependencyScannerService(upkRepository);

        PrototypeMergerSessionStore.PrototypeMergerSessionData savedSession = PrototypeMergerSessionStore.Load();
        client148Root = savedSession.Client148Root;
        client152Root = savedSession.Client152Root;
        sourceUpkPath = savedSession.SourceUpkPath;
        SyncSourceUpkSelectionFromText(sourceUpkPath);

        loadReportCommand = new AsyncRelayCommand(LoadReportAsync);
        discoverPrototypesCommand = new AsyncRelayCommand(DiscoverPrototypesAsync, CanDiscover);
        buildMergePlansCommand = new AsyncRelayCommand(BuildMergePlansAsync, CanBuildPlans);
        runMergeCommand = new AsyncRelayCommand(RunMergeAsync, CanRunMerge);
        runAutoPipelineCommand = new AsyncRelayCommand(RunAutoPipelineAsync, CanRunAutoPipeline);
        browseSourceUpkCommand = new AsyncRelayCommand(BrowseSourceUpkAsync);
        browseClient148RootCommand = new AsyncRelayCommand(BrowseClient148RootAsync);
        browseClient152RootCommand = new AsyncRelayCommand(BrowseClient152RootAsync);
    }

    public async Task LoadReportAsync()
    {
        if (BrowseReportRequestedAsync is null)
        {
            StatusText = "No report picker is configured.";
            return;
        }

        string? path = await BrowseReportRequestedAsync().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            StatusText = "No report was selected.";
            return;
        }

        string extension = Path.GetExtension(path);
        if (extension.Equals(".upk", StringComparison.OrdinalIgnoreCase))
        {
            await LoadReportFromUpkAsync(path).ConfigureAwait(true);
            return;
        }

        await LoadReportFromPathAsync(path).ConfigureAwait(true);
    }

    public async Task LoadReportFromPathAsync(string path)
    {
        try
        {
            string json = await File.ReadAllTextAsync(path).ConfigureAwait(true);
            ThanosDependencyReport? report = JsonSerializer.Deserialize<ThanosDependencyReport>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (report is null)
            {
                StatusText = "Dependency report could not be loaded.";
                return;
            }

            report.FilePath = Path.GetFullPath(path);
            SelectedReport = report;
            StatusText = $"Loaded dependency report with {report.MissingDependencyCount:N0} missing dependency item(s).";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load dependency report: {ex.Message}";
        }
    }

    public async Task LoadReportFromUpkAsync(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                StatusText = "Source UPK could not be found.";
                return;
            }

            if (string.IsNullOrWhiteSpace(Client152Root))
            {
                StatusText = "Select the 1.52 client root before scanning a source UPK.";
                return;
            }

            ThanosDependencyReport report = await dependencyScannerService.ScanDependenciesAsync(fullPath, Client152Root!).ConfigureAwait(true);

            SelectedReport = report;
            StatusText = $"Scanned source UPK and found {report.MissingDependencyCount:N0} missing dependency item(s).";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to generate dependency report from UPK: {ex.Message}";
        }
    }

    public async Task DiscoverPrototypesAsync()
    {
        if (!CanDiscover())
        {
            StatusText = "Load a dependency report, then select the 1.48 client root for prototype discovery.";
            return;
        }

        IsDiscovering = true;
        DiscoveryProgressValue = 0;
        DiscoveryProgressMaximum = Math.Max(1, SelectedReport?.MissingDependencyCount ?? 1);
        DiscoveryCurrentFile = string.Empty;
        DiscoveryStatus = "Starting prototype discovery...";
        try
        {
            StatusText = "Discovering prototypes in 1.48...";
            DiscoveryStatus = "Scanning CookedPCConsole, MarvelGame, and Engine roots where available...";
            IProgress<ThanosDiscoveryProgress> progress = new Progress<ThanosDiscoveryProgress>(progressItem =>
            {
                // Throttle aggressive per-item UI updates to avoid WinUI layout-cycle crashes.
                if (!ShouldApplyDiscoveryUiTick(progressItem))
                    return;

                DiscoveryProgressMaximum = Math.Max(1, progressItem.TotalItems);
                DiscoveryProgressValue = Math.Min(DiscoveryProgressMaximum, progressItem.ProcessedItems);
                DiscoveryCurrentFile = progressItem.CurrentFile;
                DiscoveryStatus = progressItem.Status;
                if (!string.IsNullOrWhiteSpace(progressItem.Status))
                    StatusText = progressItem.Status;
            });

            lastDiscoveryResults = await discoveryService.FindPrototypeSources(SelectedReport!, Client148Root!, progress).ConfigureAwait(true);
            ApplyDiscoveryFilter();
            MergePlans.Clear();
            MergeSteps.Clear();
            DiscoveryProgressValue = DiscoveryProgressMaximum;
            if (HiddenDiscoveryCount > 0)
            {
                DiscoveryStatus = $"Discovered {lastDiscoveryResults.Count:N0} prototype source(s). Showing first {DiscoveredPrototypes.Count:N0}; hidden {HiddenDiscoveryCount:N0}.";
                StatusText = $"Discovered {lastDiscoveryResults.Count:N0} prototype source(s). Showing {DiscoveredPrototypes.Count:N0}; hidden {HiddenDiscoveryCount:N0}.";
            }
            else
            {
                DiscoveryStatus = $"Discovered {DiscoveredPrototypes.Count:N0} prototype source(s).";
                StatusText = $"Discovered {DiscoveredPrototypes.Count:N0} prototype source(s).";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Prototype discovery failed: {ex.Message}";
        }
        finally
        {
            IsDiscovering = false;
            RefreshCommandStates();
        }
    }

    public Task BuildMergePlansAsync()
    {
        if (!CanBuildPlans())
        {
            StatusText = "Discover prototypes and select the 1.52 client root first.";
            return Task.CompletedTask;
        }

        IsMerging = true;
        try
        {
            StatusText = "Building merge plans...";
            IReadOnlyList<ThanosPrototypeMergePlan> plans = mergePlanner.BuildMergePlans(DiscoveredPrototypes.ToArray(), Client152Root!);

            MergePlans.Clear();
            foreach (ThanosPrototypeMergePlan plan in plans)
                MergePlans.Add(plan);

            MergeSteps.Clear();
            StatusText = $"Built {MergePlans.Count:N0} merge plan(s).";
        }
        catch (Exception ex)
        {
            StatusText = $"Merge planning failed: {ex.Message}";
        }
        finally
        {
            IsMerging = false;
            RefreshCommandStates();
        }

        return Task.CompletedTask;
    }

    public async Task RunMergeAsync()
    {
        if (!CanRunMerge())
        {
            StatusText = "Build merge plans before running the merger.";
            return;
        }

        IsMerging = true;
        try
        {
            StatusText = "Running prototype merge...";
            IReadOnlyList<ThanosMigrationStep> steps = await mergerService.MergePrototypes(MergePlans.ToArray(), Client152Root!).ConfigureAwait(true);
            IReadOnlyList<ThanosMigrationStep> sizeValidationSteps = BuildSizeValidationSteps(MergePlans.ToArray());

            MergeSteps.Clear();
            foreach (ThanosMigrationStep step in steps)
                MergeSteps.Add(step);
            foreach (ThanosMigrationStep validationStep in sizeValidationSteps)
                MergeSteps.Add(validationStep);

            int failedSizeChecks = sizeValidationSteps.Count(step => step.Status == ThanosMigrationStepStatus.Failed);
            StatusText = failedSizeChecks > 0
                ? $"Merge complete. Size validation failed for {failedSizeChecks:N0} package(s)."
                : $"Merge complete with {MergeSteps.Count:N0} step(s).";
        }
        catch (Exception ex)
        {
            StatusText = $"Prototype merge failed: {ex.Message}";
        }
        finally
        {
            IsMerging = false;
            RefreshCommandStates();
        }
    }

    private static IReadOnlyList<ThanosMigrationStep> BuildSizeValidationSteps(IReadOnlyList<ThanosPrototypeMergePlan> plans)
    {
        List<ThanosMigrationStep> steps = [];
        foreach (ThanosPrototypeMergePlan plan in plans)
        {
            string targetPath = Path.GetFullPath(plan.TargetUpkPath);
            string? sourcePath = SelectPrimarySourceUpkForSizeCheck(plan);

            ThanosMigrationStep step = new()
            {
                Name = "SizeValidation",
                Description = $"Compare source/target file size for {Path.GetFileName(targetPath)}."
            };

            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                step.Status = ThanosMigrationStepStatus.Skipped;
                step.Reason = "Source UPK missing for comparison.";
                steps.Add(step);
                continue;
            }

            if (!File.Exists(targetPath))
            {
                step.Status = ThanosMigrationStepStatus.Failed;
                step.Reason = $"Target UPK missing: {targetPath}";
                steps.Add(step);
                continue;
            }

            long sourceLength = new FileInfo(sourcePath).Length;
            long targetLength = new FileInfo(targetPath).Length;
            long delta = targetLength - sourceLength;
            bool strictSameName = string.Equals(
                                      Path.GetFileName(sourcePath),
                                      Path.GetFileName(targetPath),
                                      StringComparison.OrdinalIgnoreCase) &&
                                  (Path.GetFileName(targetPath).Contains("Thanos", StringComparison.OrdinalIgnoreCase) ||
                                   Path.GetFileName(targetPath).Contains("Knowhere", StringComparison.OrdinalIgnoreCase));

            double tolerance = strictSameName
                ? 0d
                : Math.Max(8192d, sourceLength * 0.12d);
            bool pass = Math.Abs(delta) <= tolerance;

            step.Status = pass ? ThanosMigrationStepStatus.Done : ThanosMigrationStepStatus.Failed;
            step.Reason = $"Source={sourceLength:N0} bytes | Target={targetLength:N0} bytes | Delta={delta:+#,0;-#,0;0} | Tol={tolerance:N0}";
            steps.Add(step);
        }

        return steps;
    }

    private static string? SelectPrimarySourceUpkForSizeCheck(ThanosPrototypeMergePlan plan)
    {
        if (plan.SourcePrototypes.Count == 0)
            return null;

        string targetName = Path.GetFileName(plan.TargetUpkPath);
        ThanosPrototypeSource? sameName = plan.SourcePrototypes.FirstOrDefault(source =>
            string.Equals(Path.GetFileName(source.SourceUpkPath), targetName, StringComparison.OrdinalIgnoreCase));

        return sameName?.SourceUpkPath ?? plan.SourcePrototypes[0].SourceUpkPath;
    }

    public async Task RunAutoPipelineAsync()
    {
        if (!CanRunAutoPipeline())
        {
            StatusText = "Select source UPK + 1.48 root + 1.52 root before running auto pipeline.";
            return;
        }

        IReadOnlyList<string> sourceUpks = GetSourceUpkPaths();
        if (sourceUpks.Count == 0)
        {
            StatusText = "No valid source UPK paths were found.";
            return;
        }

        int succeeded = 0;
        int failed = 0;

        foreach (string sourceUpk in sourceUpks)
        {
            try
            {
                int current = succeeded + failed + 1;
                await RunAutoPipelineForSourceAsync(sourceUpk, current, sourceUpks.Count).ConfigureAwait(true);
                succeeded++;
            }
            catch (Exception ex)
            {
                failed++;
                App.WriteDiagnosticsLog("ThanosPrototypeMerger.BatchAutoRun", $"{sourceUpk}{Environment.NewLine}{ex}");
            }
        }

        StatusText = $"Auto pipeline complete. Total={sourceUpks.Count:N0}, Succeeded={succeeded:N0}, Failed={failed:N0}.";
    }

    private async Task RunAutoPipelineForSourceAsync(string sourceUpk, int ordinal, int total)
    {
        string sourceName = Path.GetFileName(sourceUpk);
        StatusText = $"Auto pipeline [{ordinal}/{total}]: scanning {sourceName}...";

        ThanosDependencyReport report = await dependencyScannerService
            .ScanDependenciesAsync(Path.GetFullPath(sourceUpk), Client152Root!)
            .ConfigureAwait(true);

        if (report is null)
            throw new InvalidOperationException($"Dependency report generation failed for {sourceName}.");

        StatusText = $"Auto pipeline [{ordinal}/{total}]: discovering prototypes for {sourceName}...";
        IReadOnlyList<ThanosPrototypeSource> discovered = await discoveryService
            .FindPrototypeSources(report, Client148Root!, progress: null)
            .ConfigureAwait(true);

        IReadOnlyList<ThanosPrototypeSource> filtered = ShowOnlyRaidRelevant
            ? discovered.Where(static source => source.IsRaidRelevant).ToArray()
            : discovered;

        if (filtered.Count == 0)
            throw new InvalidOperationException($"No prototypes discovered for {sourceName}.");

        StatusText = $"Auto pipeline [{ordinal}/{total}]: building merge plans for {sourceName}...";
        IReadOnlyList<ThanosPrototypeMergePlan> plans = mergePlanner.BuildMergePlans(filtered, Client152Root!);
        if (plans.Count == 0)
            throw new InvalidOperationException($"No merge plans built for {sourceName}.");

        StatusText = $"Auto pipeline [{ordinal}/{total}]: merging {sourceName}...";
        IReadOnlyList<ThanosMigrationStep> mergeSteps = await mergerService
            .MergePrototypes(plans, Client152Root!)
            .ConfigureAwait(true);
        IReadOnlyList<ThanosMigrationStep> sizeValidationSteps = BuildSizeValidationSteps(plans);

        // Update UI once per source to avoid LayoutCycleException from high-frequency collection churn.
        lastDiscoveryResults = filtered;
        ApplyDiscoveryFilter();

        MergePlans.Clear();
        foreach (ThanosPrototypeMergePlan plan in plans)
            MergePlans.Add(plan);

        MergeSteps.Clear();
        foreach (ThanosMigrationStep step in mergeSteps)
            MergeSteps.Add(step);
        foreach (ThanosMigrationStep validationStep in sizeValidationSteps)
            MergeSteps.Add(validationStep);

        int failedSizeChecks = sizeValidationSteps.Count(step => step.Status == ThanosMigrationStepStatus.Failed);
        StatusText = failedSizeChecks > 0
            ? $"Auto pipeline [{ordinal}/{total}] finished {sourceName} with {failedSizeChecks:N0} size validation failure(s)."
            : $"Auto pipeline [{ordinal}/{total}] finished {sourceName}.";
    }

    public async Task BrowseClient148RootAsync()
    {
        if (BrowseClient148RootRequestedAsync is null)
            return;

        string? path = await BrowseClient148RootRequestedAsync().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path))
            Client148Root = path;
    }

    public async Task BrowseClient152RootAsync()
    {
        if (BrowseClient152RootRequestedAsync is null)
            return;

        string? path = await BrowseClient152RootRequestedAsync().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path))
            Client152Root = path;
    }

    public async Task BrowseSourceUpkAsync()
    {
        if (BrowseSourceUpksRequestedAsync is null)
            return;

        IReadOnlyList<string> paths = await BrowseSourceUpksRequestedAsync().ConfigureAwait(true);
        if (paths.Count == 0)
            return;

        List<string> normalized = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        SourceUpkPath = string.Join(";", normalized);
    }

    private bool CanDiscover()
        => !IsMerging &&
           !IsDiscovering &&
           SelectedReport is not null &&
           !string.IsNullOrWhiteSpace(Client148Root);

    private bool CanBuildPlans()
        => !IsMerging &&
           DiscoveredPrototypes.Count > 0 &&
           !string.IsNullOrWhiteSpace(Client152Root);

    private bool CanRunMerge()
        => !IsMerging &&
           MergePlans.Count > 0 &&
           !string.IsNullOrWhiteSpace(Client152Root);

    private bool CanRunAutoPipeline()
        => !IsMerging &&
           !IsDiscovering &&
           GetSourceUpkPaths().Count > 0 &&
           !string.IsNullOrWhiteSpace(Client148Root) &&
           !string.IsNullOrWhiteSpace(Client152Root);

    private void RefreshCommandStates()
    {
        discoverPrototypesCommand.NotifyCanExecuteChanged();
        buildMergePlansCommand.NotifyCanExecuteChanged();
        runMergeCommand.NotifyCanExecuteChanged();
        runAutoPipelineCommand.NotifyCanExecuteChanged();
    }

    private void ApplyDiscoveryFilter()
    {
        DiscoveredPrototypes.Clear();
        HiddenDiscoveryCount = 0;

        int added = 0;
        foreach (ThanosPrototypeSource source in lastDiscoveryResults)
        {
            if (ShowOnlyRaidRelevant && !source.IsRaidRelevant)
                continue;

            if (added >= MaxVisiblePrototypeRows)
            {
                HiddenDiscoveryCount++;
                continue;
            }

            DiscoveredPrototypes.Add(source);
            added++;
        }

        OnPropertyChanged(nameof(DiscoveredPrototypes));
    }

    private int lastUiProgressProcessed = -1;
    private DateTime lastUiProgressAtUtc = DateTime.MinValue;

    private IReadOnlyList<string> GetSourceUpkPaths()
    {
        if (selectedSourceUpkPaths.Count > 0)
            return selectedSourceUpkPaths;

        if (string.IsNullOrWhiteSpace(SourceUpkPath))
            return [];

        return ParseSourceUpkPaths(SourceUpkPath)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void SyncSourceUpkSelectionFromText(string? raw)
    {
        List<string> parsed = ParseSourceUpkPaths(raw);
        selectedSourceUpkPaths.Clear();
        selectedSourceUpkPaths.AddRange(parsed.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase));
        UpdateSourceUpkSelectionText();
    }

    private void UpdateSourceUpkSelectionText()
    {
        if (selectedSourceUpkPaths.Count == 0)
        {
            SourceUpkSelectionText = "No source UPKs selected.";
            return;
        }

        if (selectedSourceUpkPaths.Count == 1)
        {
            SourceUpkSelectionText = $"1 source UPK selected: {Path.GetFileName(selectedSourceUpkPaths[0])}";
            return;
        }

        SourceUpkSelectionText = $"{selectedSourceUpkPaths.Count:N0} source UPKs selected. First: {Path.GetFileName(selectedSourceUpkPaths[0])}";
    }

    private static List<string> ParseSourceUpkPaths(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        return raw
            .Split(['\r', '\n', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(path =>
            {
                try { return Path.GetFullPath(path); } catch { return string.Empty; }
            })
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();
    }

    private bool ShouldApplyDiscoveryUiTick(ThanosDiscoveryProgress progressItem)
    {
        DateTime now = DateTime.UtcNow;
        bool processedChanged = progressItem.ProcessedItems != lastUiProgressProcessed;
        bool tickDue = (now - lastUiProgressAtUtc).TotalMilliseconds >= 90;
        if (!processedChanged && !tickDue)
            return false;

        lastUiProgressProcessed = progressItem.ProcessedItems;
        lastUiProgressAtUtc = now;
        return true;
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

    private sealed class AsyncRelayCommand : IAsyncRelayCommand
    {
        private readonly Func<Task> execute;
        private readonly Func<bool>? canExecute;

        public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        {
            this.execute = execute;
            this.canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

        public async void Execute(object? parameter)
        {
            try
            {
                await ExecuteAsync(parameter).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                OmegaAssetStudio.WinUI.App.WriteDiagnosticsLog("ThanosPrototypeMerger.AsyncRelayCommand", ex.ToString());
            }
        }

        public Task ExecuteAsync(object? parameter = null)
        {
            return execute();
        }

        public void NotifyCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public interface IAsyncRelayCommand : ICommand
{
    Task ExecuteAsync(object? parameter = null);

    void NotifyCanExecuteChanged();
}

