using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.Commands;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.Core;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.Interop;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialServices;
using OmegaAssetStudio.WinUI.Models;
using OmegaAssetStudio.MeshPreview;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor;

public sealed class MaterialEditorViewModel : Core.NotifyPropertyChangedBase
{
    private readonly MaterialEditorService materialEditorService;
    private readonly ITextureEditorService textureEditorService;
    private readonly LoadMaterialCommand loadMaterialCommand;
    private readonly SaveMaterialCommand saveMaterialCommand;
    private readonly SelectTextureSlotCommand selectTextureSlotCommand;
    private readonly OpenTextureInTextures2Command openTextureInTextures2Command;
    private readonly ReplaceTextureSlotCommand replaceTextureSlotCommand;
    private readonly ResetParameterCommand resetParameterCommand;
    private MaterialEditorContext? context;
    private readonly Stack<MaterialDefinition> undoHistory = new();
    private const int MaxUndoDepth = 32;
    private bool isApplyingUndo;

    private ObservableCollection<MaterialDefinition> materials = new();
    private ObservableCollection<string> previewSkeletalMeshExports = new();
    private ObservableCollection<string> previewLodOptions = new(["LOD 0"]);
    private ObservableCollection<TextureMipOption> selectedTextureMipOptions = new();
    private readonly Dictionary<string, MaterialDefinition> originalMaterialSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private MaterialDefinition? selectedMaterial;
    private MaterialTextureSlot? selectedTextureSlot;
    private TextureMipOption? selectedTextureMipOption;
    private MaterialParameter? selectedParameter;
    private MaterialPreviewConfig previewConfig = new();
    private string previewMeshUpkPath = string.Empty;
    private string previewMeshExportPath = string.Empty;
    private string? selectedPreviewSkeletalMeshExportPath;
    private int selectedPreviewLodIndex;
    private int previewRefreshToken;
    private string loadedUpkText = "No UPK loaded.";
    private string statusText = "Ready.";
    private string selectedMaterialSummary = "No material selected.";
    private string selectedTextureMetadataText = "Select a texture slot to inspect.";
    private int? selectedTextureMipAbsoluteIndex;

    public MaterialEditorViewModel()
        : this(new MaterialEditorService(), new Textures2TextureEditorServiceAdapter())
    {
    }

    public MaterialEditorViewModel(MaterialEditorService materialEditorService, ITextureEditorService textureEditorService)
    {
        this.materialEditorService = materialEditorService;
        this.textureEditorService = textureEditorService;

        loadMaterialCommand = new LoadMaterialCommand(LoadMaterialsAsync);
        saveMaterialCommand = new SaveMaterialCommand(SaveSelectedMaterialAsync, CanSaveSelectedMaterial);
        selectTextureSlotCommand = new SelectTextureSlotCommand(SelectTextureSlot);
        openTextureInTextures2Command = new OpenTextureInTextures2Command(OpenTextureInTextures2Async, CanOpenTexture);
        replaceTextureSlotCommand = new ReplaceTextureSlotCommand(ReplaceTextureSlotAsync, CanOpenTexture);
        resetParameterCommand = new ResetParameterCommand(ResetParameter);
    }

    public MaterialEditorContext? Context
    {
        get => context;
        private set => SetProperty(ref context, value);
    }

    public void AttachContext(MaterialEditorContext materialEditorContext)
    {
        Context = materialEditorContext;
        Context?.PublishMaterial(SelectedMaterial);
    }

    public ObservableCollection<MaterialDefinition> Materials
    {
        get => materials;
        private set => SetProperty(ref materials, value);
    }

    public MaterialDefinition? SelectedMaterial
    {
        get => selectedMaterial;
        set
        {
            if (!SetProperty(ref selectedMaterial, value))
                return;

            undoHistory.Clear();
            OnPropertyChanged(nameof(CanUndo));
            selectedTextureMipAbsoluteIndex = null;
            SelectedTextureMipOptions = new ObservableCollection<TextureMipOption>();
            SelectedTextureMipOption = null;

            SelectedMaterialSummary = BuildMaterialSummary(value);
            SelectedTextureSlot = value?.TextureSlots.FirstOrDefault();
            SelectedParameter = value?.ScalarParameters.FirstOrDefault() ?? value?.VectorParameters.FirstOrDefault();
            Context?.PublishMaterial(value);
            if (value is not null)
            {
                if (!string.IsNullOrWhiteSpace(value.SourceUpkPath))
                    PreviewMeshUpkPath = value.SourceUpkPath;
                _ = LoadPreviewMeshExportsAsync(value.SourceUpkPath, value.SourceMeshExportPath);
                SetPreviewChannelForSlot(SelectedTextureSlot, false);
            }

            saveMaterialCommand.NotifyCanExecuteChanged();
            openTextureInTextures2Command.NotifyCanExecuteChanged();
            _ = RefreshSelectedTextureMetadataAsync();
        }
    }

    public ObservableCollection<string> PreviewSkeletalMeshExports
    {
        get => previewSkeletalMeshExports;
        private set => SetProperty(ref previewSkeletalMeshExports, value);
    }

    public ObservableCollection<string> PreviewLodOptions
    {
        get => previewLodOptions;
        private set => SetProperty(ref previewLodOptions, value);
    }

    public ObservableCollection<TextureMipOption> SelectedTextureMipOptions
    {
        get => selectedTextureMipOptions;
        private set => SetProperty(ref selectedTextureMipOptions, value);
    }

    public MaterialTextureSlot? SelectedTextureSlot
    {
        get => selectedTextureSlot;
        set
        {
            if (!SetProperty(ref selectedTextureSlot, value))
                return;

            OnPropertyChanged(nameof(PreviewTextureSlotPath));
        }
    }

    public TextureMipOption? SelectedTextureMipOption
    {
        get => selectedTextureMipOption;
        set
        {
            if (!SetProperty(ref selectedTextureMipOption, value))
                return;

            selectedTextureMipAbsoluteIndex = value?.AbsoluteIndex;
            OnPropertyChanged(nameof(PreviewTextureMipIndex));
            RequestPreviewRefresh();
        }
    }

    public MaterialParameter? SelectedParameter
    {
        get => selectedParameter;
        set => SetProperty(ref selectedParameter, value);
    }

    public MaterialPreviewConfig PreviewConfig
    {
        get => previewConfig;
        set => SetProperty(ref previewConfig, value);
    }

    public string PreviewMeshUpkPath
    {
        get => previewMeshUpkPath;
        set
        {
            if (!SetProperty(ref previewMeshUpkPath, value))
                return;

            RequestPreviewRefresh();
        }
    }

    public string PreviewMeshExportPath
    {
        get => previewMeshExportPath;
        set
        {
            if (!SetProperty(ref previewMeshExportPath, value))
                return;

            if (!string.Equals(selectedPreviewSkeletalMeshExportPath, value, StringComparison.OrdinalIgnoreCase))
                selectedPreviewSkeletalMeshExportPath = value;

            OnPropertyChanged(nameof(PreviewMeshSelectionText));
            RequestPreviewRefresh();
        }
    }

    public string? SelectedPreviewSkeletalMeshExportPath
    {
        get => selectedPreviewSkeletalMeshExportPath;
        set
        {
            if (string.Equals(selectedPreviewSkeletalMeshExportPath, value, StringComparison.OrdinalIgnoreCase))
                return;

            selectedPreviewSkeletalMeshExportPath = value;
            OnPropertyChanged();

            if (!string.IsNullOrWhiteSpace(value) && !string.Equals(previewMeshExportPath, value, StringComparison.OrdinalIgnoreCase))
                PreviewMeshExportPath = value;

            _ = LoadPreviewLodOptionsAsync(PreviewMeshUpkPath, value);
            OnPropertyChanged(nameof(PreviewMeshSelectionText));
        }
    }

    public int SelectedPreviewLodIndex
    {
        get => selectedPreviewLodIndex;
        set
        {
            if (!SetProperty(ref selectedPreviewLodIndex, value))
                return;

            RequestPreviewRefresh();
        }
    }

    public int PreviewRefreshToken
    {
        get => previewRefreshToken;
        private set => SetProperty(ref previewRefreshToken, value);
    }

    public string LoadedUpkText
    {
        get => loadedUpkText;
        set => SetProperty(ref loadedUpkText, value);
    }

    public string StatusText
    {
        get => statusText;
        set => SetProperty(ref statusText, value);
    }

    public string SelectedMaterialSummary
    {
        get => selectedMaterialSummary;
        set => SetProperty(ref selectedMaterialSummary, value);
    }

    public string SelectedTextureMetadataText
    {
        get => selectedTextureMetadataText;
        set => SetProperty(ref selectedTextureMetadataText, value);
    }

    public string PreviewMeshSelectionText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(SelectedPreviewSkeletalMeshExportPath))
                return $"Preview mesh: {SelectedPreviewSkeletalMeshExportPath}";

            if (!string.IsNullOrWhiteSpace(PreviewMeshExportPath))
                return $"Preview mesh: {PreviewMeshExportPath}";

            return "Preview mesh will follow the selected material's source export.";
        }
    }

    public string PreviewTextureSlotPath => SelectedTextureSlot?.TexturePath ?? string.Empty;

    public int PreviewTextureMipIndex => selectedTextureMipAbsoluteIndex ?? -1;

    public bool CanUndo => undoHistory.Count > 0;

    public ICommand LoadMaterialCommand => loadMaterialCommand;

    public ICommand SaveMaterialCommand => saveMaterialCommand;

    public ICommand SelectTextureSlotCommand => selectTextureSlotCommand;

    public ICommand OpenTextureInTextures2Command => openTextureInTextures2Command;

    public ICommand ReplaceTextureSlotCommand => replaceTextureSlotCommand;

    public ICommand ResetParameterCommand => resetParameterCommand;

    public void CaptureUndoCheckpoint()
    {
        if (SelectedMaterial is null || isApplyingUndo)
            return;

        PushUndoSnapshot(SelectedMaterial.Clone());
    }

    public void UndoLastChange()
    {
        if (SelectedMaterial is null || undoHistory.Count == 0)
            return;

        MaterialDefinition snapshot = undoHistory.Pop();
        isApplyingUndo = true;
        try
        {
            SelectedMaterial.CopyFrom(snapshot);
        }
        finally
        {
            isApplyingUndo = false;
        }

        SelectedMaterialSummary = BuildMaterialSummary(SelectedMaterial);
        SelectedTextureSlot = SelectedMaterial.TextureSlots.FirstOrDefault();
        SelectedParameter = SelectedMaterial.ScalarParameters.FirstOrDefault() ?? SelectedMaterial.VectorParameters.FirstOrDefault();
        Context?.PublishMaterial(SelectedMaterial);
        OnPropertyChanged(nameof(CanUndo));
        _ = RefreshSelectedTextureMetadataAsync();
        RequestPreviewRefresh();
        StatusText = "Undo applied.";
    }

    public async Task LoadUpkAsync(string upkPath)
    {
        if (string.IsNullOrWhiteSpace(upkPath))
            return;

        await LoadMaterialsFromPathsAsync([upkPath]).ConfigureAwait(true);
    }

    public async Task LoadMaterialsFromPathsAsync(IEnumerable<string> upkPaths)
    {
        List<string> loadedFiles = [];
        List<MaterialDefinition> loadedMaterials = [];

        Materials.Clear();
        materialEditorService.Clear();
        originalMaterialSnapshots.Clear();
        undoHistory.Clear();
        OnPropertyChanged(nameof(CanUndo));

        foreach (string upkPath in upkPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            StatusText = $"Loading {Path.GetFileName(upkPath)}...";
            try
            {
                IReadOnlyList<MaterialDefinition> materials = await materialEditorService.LoadMaterialsFromUpkAsync(upkPath).ConfigureAwait(true);
                foreach (MaterialDefinition material in materials)
                {
                    Materials.Add(material);
                    originalMaterialSnapshots[material.Path] = material.Clone();
                    loadedMaterials.Add(material);
                }

                loadedFiles.Add(Path.GetFileName(upkPath));
            }
            catch (Exception ex)
            {
                StatusText = $"Failed to load {Path.GetFileName(upkPath)}.";
                App.WriteDiagnosticsLog("MaterialEditor.Load", ex.ToString());
            }
        }

        LoadedUpkText = loadedFiles.Count == 0 ? "No UPK loaded." : string.Join("\n", loadedFiles);
        SelectedMaterial = Materials.FirstOrDefault();
        StatusText = $"Loaded {Materials.Count} material(s).";
        saveMaterialCommand.NotifyCanExecuteChanged();
        openTextureInTextures2Command.NotifyCanExecuteChanged();
    }

    public async Task SaveSelectedMaterialAsync()
    {
        if (SelectedMaterial is null)
            return;

        MaterialValidationResult definitionValidation = materialEditorService.ValidateMaterialDefinition(SelectedMaterial);
        if (!definitionValidation.IsValid)
        {
            StatusText = $"Validation failed for {SelectedMaterial.Name}.";
            App.WriteDiagnosticsLog("MaterialEditor.Validation", $"{definitionValidation.MaterialName}: {definitionValidation.Message}");
            return;
        }

        StatusText = $"Saving {SelectedMaterial.Name}...";
        await materialEditorService.SaveMaterialAsync(SelectedMaterial).ConfigureAwait(true);
        MaterialValidationResult validation = await materialEditorService.ValidateMaterialRoundTripAsync(SelectedMaterial).ConfigureAwait(true);
        if (validation.IsValid)
        {
            StatusText = $"Saved and validated {SelectedMaterial.Name}.";
            App.WriteDiagnosticsLog("MaterialEditor.Validation", $"{validation.MaterialName}: {validation.Message}");
        }
        else
        {
            StatusText = $"Saved {SelectedMaterial.Name}, validation warning.";
            App.WriteDiagnosticsLog("MaterialEditor.Validation", $"{validation.MaterialName}: {validation.Message}");
        }
    }

    public void SelectTextureSlot(MaterialTextureSlot? slot)
    {
        SelectedTextureSlot = slot;
        selectedTextureMipAbsoluteIndex = null;
        SelectedTextureMipOption = null;
        SetPreviewChannelForSlot(slot, false);
        openTextureInTextures2Command.NotifyCanExecuteChanged();
        _ = RefreshSelectedTextureMetadataAsync();
        RequestPreviewRefresh();
    }

    public async Task OpenTextureInTextures2Async(MaterialTextureSlot? slot)
    {
        MaterialTextureSlot? target = slot ?? SelectedTextureSlot;
        if (target is null)
            return;

        try
        {
            StatusText = $"Opening {target.SlotName} in Textures 2.0...";
            await textureEditorService.OpenTextureAsync(target.TextureName, target.TexturePath, GetSelectedTextureSourceUpkPath()).ConfigureAwait(true);
            StatusText = $"Opened {target.SlotName} in Textures 2.0.";
        }
        catch (Exception ex)
        {
            StatusText = $"Open in Textures 2.0 failed for {target.SlotName}.";
            App.WriteDiagnosticsLog("MaterialEditor.OpenTexture", ex.ToString());
        }
    }

    public async Task ReplaceTextureSlotAsync(MaterialTextureSlot? slot)
    {
        MaterialTextureSlot? target = slot ?? SelectedTextureSlot;
        if (target is null)
            return;

        string? replacementPath = await textureEditorService.BrowseForNewTextureAsync().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(replacementPath))
            return;

        if (SelectedMaterial is not null)
            PushUndoSnapshot(SelectedMaterial.Clone());

        StatusText = $"Replacing {target.SlotName}...";
        try
        {
            await textureEditorService.ReplaceTextureAsync(target.TextureName, target.TexturePath, GetSelectedTextureSourceUpkPath(), replacementPath).ConfigureAwait(true);
            StatusText = $"Replaced {target.SlotName}.";
            await RefreshSelectedTextureMetadataAsync().ConfigureAwait(true);
            RequestPreviewRefresh();
        }
        catch (Exception ex)
        {
            StatusText = $"Replace failed for {target.SlotName}.";
            App.WriteDiagnosticsLog("MaterialEditor.ReplaceTexture", ex.ToString());
        }
    }

    public void ResetParameter(MaterialParameter? parameter)
    {
        if (parameter is null)
            return;

        if (SelectedMaterial is not null)
            PushUndoSnapshot(SelectedMaterial.Clone());

        if (parameter.Category.Equals("Scalar", StringComparison.OrdinalIgnoreCase))
            parameter.ScalarValue = parameter.DefaultScalarValue;
        else
            parameter.VectorValue = parameter.DefaultVectorValue;

        SelectedMaterialSummary = BuildMaterialSummary(SelectedMaterial);
        RequestPreviewRefresh();
    }

    public void ResetSelectedMaterial()
    {
        if (SelectedMaterial is null)
            return;

        PushUndoSnapshot(SelectedMaterial.Clone());

        if (!originalMaterialSnapshots.TryGetValue(SelectedMaterial.Path, out MaterialDefinition? snapshot))
            return;

        SelectedMaterial.CopyFrom(snapshot);
        SelectedMaterialSummary = BuildMaterialSummary(SelectedMaterial);
            SelectedTextureSlot = SelectedMaterial.TextureSlots.FirstOrDefault();
            SelectedParameter = SelectedMaterial.ScalarParameters.FirstOrDefault() ?? SelectedMaterial.VectorParameters.FirstOrDefault();
            PreviewConfig = new MaterialPreviewConfig
            {
                LightDirection = PreviewConfig.LightDirection,
            LightIntensity = PreviewConfig.LightIntensity,
            LightColor = PreviewConfig.LightColor,
                BackgroundColor = PreviewConfig.BackgroundColor,
                MeshType = PreviewConfig.MeshType,
                MaterialChannel = nameof(MeshPreviewMaterialChannel.FullMaterial)
            };
            SelectedPreviewLodIndex = 0;
            _ = RefreshSelectedTextureMetadataAsync();
            RequestPreviewRefresh();
        }

    public async Task LoadFromWorkspaceContextAsync(WorkspaceLaunchContext? context)
    {
        if (context is null || string.IsNullOrWhiteSpace(context.UpkPath))
            return;

        PreviewMeshUpkPath = context.UpkPath;
        PreviewMeshExportPath = context.ExportPath;
        await LoadUpkAsync(context.UpkPath).ConfigureAwait(true);
    }

    private async Task LoadPreviewMeshExportsAsync(string? upkPath, string? preferredExportPath)
    {
        if (string.IsNullOrWhiteSpace(upkPath) || !File.Exists(upkPath))
        {
            PreviewSkeletalMeshExports = new ObservableCollection<string>();
            SelectedPreviewSkeletalMeshExportPath = null;
            OnPropertyChanged(nameof(PreviewMeshSelectionText));
            return;
        }

        try
        {
            IReadOnlyList<string> exports = await materialEditorService.GetSkeletalMeshExportsAsync(upkPath).ConfigureAwait(true);
            PreviewSkeletalMeshExports = new ObservableCollection<string>(exports);

            string? selection = !string.IsNullOrWhiteSpace(preferredExportPath) && exports.Any(path => string.Equals(path, preferredExportPath, StringComparison.OrdinalIgnoreCase))
                ? preferredExportPath
                : exports.FirstOrDefault();

            SelectedPreviewSkeletalMeshExportPath = selection;
            if (!string.IsNullOrWhiteSpace(selection))
                PreviewMeshExportPath = selection;
            OnPropertyChanged(nameof(PreviewMeshSelectionText));
        }
        catch (Exception ex)
        {
            PreviewSkeletalMeshExports = new ObservableCollection<string>();
            PreviewLodOptions = new ObservableCollection<string>(["LOD 0"]);
            SelectedPreviewLodIndex = 0;
            SelectedPreviewSkeletalMeshExportPath = null;
            OnPropertyChanged(nameof(PreviewMeshSelectionText));
            App.WriteDiagnosticsLog("MaterialEditor.PreviewExports", ex.ToString());
        }
    }

    private async Task LoadPreviewLodOptionsAsync(string? upkPath, string? exportPath)
    {
        if (string.IsNullOrWhiteSpace(upkPath) || string.IsNullOrWhiteSpace(exportPath) || !File.Exists(upkPath))
        {
            PreviewLodOptions = new ObservableCollection<string>(["LOD 0"]);
            SelectedPreviewLodIndex = 0;
            OnPropertyChanged(nameof(PreviewMeshSelectionText));
            return;
        }

        try
        {
            int lodCount = await materialEditorService.GetSkeletalMeshLodCountAsync(upkPath, exportPath).ConfigureAwait(true);
            int safeLodCount = Math.Max(1, lodCount);
            ObservableCollection<string> lodOptions = new(Enumerable.Range(0, safeLodCount).Select(index => $"LOD {index}"));
            PreviewLodOptions = lodOptions;
            SelectedPreviewLodIndex = Math.Clamp(SelectedPreviewLodIndex, 0, safeLodCount - 1);
        }
        catch (Exception ex)
        {
            PreviewLodOptions = new ObservableCollection<string>(["LOD 0"]);
            SelectedPreviewLodIndex = 0;
            OnPropertyChanged(nameof(PreviewMeshSelectionText));
            App.WriteDiagnosticsLog("MaterialEditor.PreviewLods", ex.ToString());
        }
    }

    private async Task RefreshSelectedTextureMetadataAsync()
    {
        MaterialTextureSlot? slot = SelectedTextureSlot ?? SelectedMaterial?.TextureSlots.FirstOrDefault();
        if (slot is null)
        {
            SelectedTextureMetadataText = "Select a texture slot to inspect.";
            SelectedTextureMipOptions = new ObservableCollection<TextureMipOption>();
            SelectedTextureMipOption = null;
            return;
        }

        string sourceUpkPath = GetSelectedTextureSourceUpkPath();
        if (string.IsNullOrWhiteSpace(sourceUpkPath))
        {
            SelectedTextureMetadataText = "No source UPK selected for texture metadata.";
            SelectedTextureMipOptions = new ObservableCollection<TextureMipOption>();
            SelectedTextureMipOption = null;
            return;
        }

        try
        {
            TextureMetadata? metadata = await textureEditorService.GetTextureMetadataAsync(slot.TextureName, slot.TexturePath, sourceUpkPath).ConfigureAwait(true);
            SelectedTextureMetadataText = BuildTextureMetadataText(metadata);
            IReadOnlyList<TextureMipOption> options = ParseMipOptions(metadata);
            SelectedTextureMipOptions = new ObservableCollection<TextureMipOption>(options);
            TextureMipOption? preferred = SelectPreferredMipOption(options, selectedTextureMipAbsoluteIndex);
            SelectedTextureMipOption = preferred;
        }
        catch (Exception ex)
        {
            SelectedTextureMetadataText = "Texture metadata unavailable.";
            SelectedTextureMipOptions = new ObservableCollection<TextureMipOption>();
            SelectedTextureMipOption = null;
            App.WriteDiagnosticsLog("MaterialEditor.TextureMetadata", ex.ToString());
        }
    }

    private string GetSelectedTextureSourceUpkPath()
    {
        if (SelectedMaterial is not null && !string.IsNullOrWhiteSpace(SelectedMaterial.SourceUpkPath))
            return SelectedMaterial.SourceUpkPath;

        return PreviewMeshUpkPath;
    }

    private async Task LoadMaterialsAsync()
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".upk");
        InitializePicker(picker);
        IReadOnlyList<Windows.Storage.StorageFile> files = await picker.PickMultipleFilesAsync();
        if (files.Count == 0)
            return;

        await LoadMaterialsFromPathsAsync(files.Select(file => file.Path)).ConfigureAwait(true);
    }

    private bool CanSaveSelectedMaterial() => SelectedMaterial is not null;

    private bool CanOpenTexture(MaterialTextureSlot? slot) => slot is not null || SelectedTextureSlot is not null;

    private void SetPreviewChannelForSlot(MaterialTextureSlot? slot, bool refreshPreview)
    {
        MaterialPreviewConfig nextConfig = new()
        {
            LightDirection = PreviewConfig.LightDirection,
            LightIntensity = PreviewConfig.LightIntensity,
            LightColor = PreviewConfig.LightColor,
            BackgroundColor = PreviewConfig.BackgroundColor,
            MeshType = PreviewConfig.MeshType,
            MaterialChannel = ResolveMaterialChannel(slot)
        };

        PreviewConfig = nextConfig;

        if (refreshPreview)
            RequestPreviewRefresh();
    }

    private static string ResolveMaterialChannel(MaterialTextureSlot? slot)
    {
        string value = $"{slot?.SlotName} {slot?.TextureName} {slot?.TexturePath}".Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(value))
            return nameof(MeshPreviewMaterialChannel.FullMaterial);

        if (value.Contains("norm"))
            return nameof(MeshPreviewMaterialChannel.Normal);

        if (value.Contains("spec"))
            return nameof(MeshPreviewMaterialChannel.Specular);

        if (value.Contains("emis") || value.Contains("glow") || value.Contains("light"))
            return nameof(MeshPreviewMaterialChannel.Emissive);

        if (value.Contains("mask") || value.Contains("alpha") || value.Contains("occlusion") || value.Contains("ao"))
            return nameof(MeshPreviewMaterialChannel.Mask);

        return nameof(MeshPreviewMaterialChannel.BaseColor);
    }

    private void PushUndoSnapshot(MaterialDefinition snapshot)
    {
        if (isApplyingUndo)
            return;

        undoHistory.Push(snapshot);
        while (undoHistory.Count > MaxUndoDepth)
        {
            // keep most recent snapshots
            MaterialDefinition[] retained = undoHistory.Take(MaxUndoDepth).Reverse().ToArray();
            undoHistory.Clear();
            foreach (MaterialDefinition item in retained)
                undoHistory.Push(item);
            break;
        }

        OnPropertyChanged(nameof(CanUndo));
    }

    private static IReadOnlyList<TextureMipOption> ParseMipOptions(TextureMetadata? metadata)
    {
        if (metadata?.MipSummaries is null || metadata.MipSummaries.Count == 0)
            return [];

        List<TextureMipOption> options = [];
        foreach (string summary in metadata.MipSummaries)
        {
            if (string.IsNullOrWhiteSpace(summary))
                continue;

            // format: "{absolute}: {width}x{height} {format} {source}"
            string[] headParts = summary.Split(':', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (headParts.Length != 2 || !int.TryParse(headParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int absolute))
                continue;

            string detail = headParts[1];
            string[] detailParts = detail.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (detailParts.Length < 3)
                continue;

            string[] dimParts = detailParts[0].Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (dimParts.Length != 2 ||
                !int.TryParse(dimParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width) ||
                !int.TryParse(dimParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height))
            {
                continue;
            }

            string format = detailParts[1];
            string source = detailParts[^1];
            options.Add(new TextureMipOption
            {
                AbsoluteIndex = absolute,
                Width = width,
                Height = height,
                Format = format,
                Source = source
            });
        }

        return options
            .OrderByDescending(option => option.Width * option.Height)
            .ThenBy(option => option.AbsoluteIndex)
            .ToArray();
    }

    private static TextureMipOption? SelectPreferredMipOption(IReadOnlyList<TextureMipOption> options, int? preferredAbsoluteIndex)
    {
        if (options.Count == 0)
            return null;

        if (preferredAbsoluteIndex.HasValue)
        {
            TextureMipOption? preferred = options.FirstOrDefault(option => option.AbsoluteIndex == preferredAbsoluteIndex.Value);
            if (preferred is not null)
                return preferred;
        }

        return options.FirstOrDefault();
    }

    private void RequestPreviewRefresh() => PreviewRefreshToken++;

    private static void InitializePicker(FileOpenPicker picker)
    {
        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);
    }

    private static string BuildMaterialSummary(MaterialDefinition? material)
    {
        if (material is null)
            return "No material selected.";

        return $"{material.Name} | {material.Type} | Textures: {material.TextureSlots.Count} | Scalars: {material.ScalarParameters.Count} | Vectors: {material.VectorParameters.Count}";
    }

    private static string BuildTextureMetadataText(TextureMetadata? metadata)
    {
        if (metadata is null)
            return "Texture metadata unavailable.";

        StringBuilder text = new();
        text.AppendLine(metadata.TextureName);
        text.AppendLine(metadata.TexturePath);
        text.AppendLine(metadata.SourceUpkPath);
        text.AppendLine($"Class: {metadata.ExportClass}");
        text.AppendLine($"Type: {metadata.ContainerType}");
        text.AppendLine($"Format: {metadata.Format}");
        text.AppendLine($"Compression: {metadata.Compression}");
        text.AppendLine($"Size: {metadata.Width} x {metadata.Height}");
        text.AppendLine($"Mips: {metadata.MipCount}");

        if (metadata.MipSummaries.Count > 0)
        {
            text.AppendLine("Mip Details:");
            foreach (string mip in metadata.MipSummaries)
                text.AppendLine(mip);
        }

        return text.ToString().TrimEnd();
    }
}

