using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OmegaAssetStudio.ThanosMigration.Services;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration;

public sealed partial class UpkMigrationView : Page
{
    public UpkMigrationViewModel ViewModel { get; }

    public UpkMigrationView()
    {
        InitializeComponent();
        DispatcherQueue? dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        UpkMigrationService migrationService = new(dispatcherQueue);
        ThanosStructuralMigrationService structuralService = new();
        ThanosTextureMigrationService textureService = new();
        ThanosTextureCacheInjectorService textureCacheInjectorService = new();
        TfcManifestService tfcManifestService = new();
        ViewModel = new UpkMigrationViewModel(migrationService, structuralService, textureService, textureCacheInjectorService, tfcManifestService, dispatcherQueue);
        ViewModel.SelectUpksRequestedAsync = SelectUpksAsync;
        ViewModel.BrowseOutputDirectoryRequestedAsync = BrowseOutputDirectoryAsync;
        ViewModel.BrowseTextureManifestDirectoryRequestedAsync = BrowseOutputDirectoryAsync;
        ViewModel.BrowseTextureSourceDirectoryRequestedAsync = BrowseOutputDirectoryAsync;
        ViewModel.BrowseResourcePrototypeSourceRequestedAsync = BrowseOutputDirectoryAsync;
        ViewModel.BrowseResourcePrototypeOutputRequestedAsync = BrowseOutputDirectoryAsync;
        ViewModel.BrowseBackportLogRequestedAsync = BrowseBackportLogAsync;
        ViewModel.BrowseBackportSourceRequestedAsync = BrowseOutputDirectoryAsync;
        ViewModel.BrowseBackportServerEmuRequestedAsync = BrowseOutputDirectoryAsync;
        ViewModel.BrowseBackportOutputRequestedAsync = BrowseOutputDirectoryAsync;
        ViewModel.BrowseBackportTargetRequestedAsync = BrowseOutputDirectoryAsync;
        ViewModel.BrowseDeploySourceRequestedAsync = BrowseDeploySourceAsync;
        ViewModel.BrowseDeployTargetRequestedAsync = BrowseOutputDirectoryAsync;
        ViewModel.PrototypeMerger.BrowseReportRequestedAsync = BrowseDependencyReportAsync;
        ViewModel.PrototypeMerger.BrowseClient148RootRequestedAsync = BrowseOutputDirectoryAsync;
        ViewModel.PrototypeMerger.BrowseClient152RootRequestedAsync = BrowseOutputDirectoryAsync;
        ViewModel.PrototypeMerger.BrowseSourceUpksRequestedAsync = BrowseDeploySourcesAsync;
        DataContext = ViewModel;
    }

    private async Task<IReadOnlyList<string>> SelectUpksAsync()
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".upk");
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        InitializePicker(picker);

        IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();
        List<string> paths = [];
        foreach (StorageFile file in files)
            paths.Add(file.Path);

        return paths;
    }

    private async Task<string?> BrowseOutputDirectoryAsync()
    {
        FolderPicker picker = new();
        picker.FileTypeFilter.Add("*");
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        InitializePicker(picker);

        StorageFolder? folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private async Task<string?> BrowseBackportLogAsync()
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".log");
        picker.FileTypeFilter.Add(".txt");
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        InitializePicker(picker);

        StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private async Task<string?> BrowseDeploySourceAsync()
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".upk");
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        InitializePicker(picker);

        StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private async Task<IReadOnlyList<string>> BrowseDeploySourcesAsync()
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".upk");
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        InitializePicker(picker);

        IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();
        List<string> paths = [];
        foreach (StorageFile file in files)
            paths.Add(file.Path);

        return paths;
    }

    private async Task<string?> BrowseDependencyReportAsync()
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".json");
        picker.FileTypeFilter.Add(".upk");
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        InitializePicker(picker);

        StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private static void InitializePicker(FileOpenPicker picker)
    {
        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);
    }

    private static void InitializePicker(FolderPicker picker)
    {
        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);
    }
}

