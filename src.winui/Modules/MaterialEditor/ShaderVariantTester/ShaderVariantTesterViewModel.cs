using System.Collections.ObjectModel;
using System.Text.Json;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialModels;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialServices;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialViewModels;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.ShaderVariantTester;

public sealed class ShaderVariantTesterViewModel : MaterialToolViewModelBase
{
    private readonly ShaderVariantService variantService;
    private MhMaterialInstance? selectedMaterial;
    private MhShaderReference? selectedShader;
    private MhShaderPermutation? selectedPermutation;
    private MhShaderPermutation? baselinePermutation;

    public ShaderVariantTesterViewModel()
        : this(new MaterialCoreServices())
    {
    }

    public ShaderVariantTesterViewModel(MaterialCoreServices services)
    {
        variantService = services.ShaderVariants;
        Title = "Shader Variant Tester";
        Variants = [];
    }

    public ObservableCollection<MhShaderPermutation> Variants { get; }

    public MhShaderReference? SelectedShader
    {
        get => selectedShader;
        set
        {
            if (!SetProperty(ref selectedShader, value))
                return;

            Variants.Clear();
            if (selectedShader is null)
            {
                BaselinePermutation = null;
                SelectedPermutation = null;
                return;
            }

            foreach (MhShaderPermutation permutation in variantService.BuildVariants(selectedShader))
                Variants.Add(permutation);

            BaselinePermutation = variantService.ResolveBaseline(Variants);
            SelectedPermutation = Variants.FirstOrDefault();
            RefreshComparisonSummary();
            RaisePropertyChanged(nameof(VariantCountText));
            RaisePropertyChanged(nameof(SelectedShaderSummaryText));
        }
    }

    public MhShaderPermutation? SelectedPermutation
    {
        get => selectedPermutation;
        set
        {
            if (!SetProperty(ref selectedPermutation, value))
                return;

            RefreshComparisonSummary();
            RaisePropertyChanged(nameof(SelectedPermutationSummaryText));
            RaisePropertyChanged(nameof(SelectedVariantDetailText));
        }
    }

    public MhShaderPermutation? BaselinePermutation
    {
        get => baselinePermutation;
        private set
        {
            if (!SetProperty(ref baselinePermutation, value))
                return;

            RefreshComparisonSummary();
            RaisePropertyChanged(nameof(BaselineVariantDetailText));
        }
    }

    public string VariantCountText => $"{Variants.Count:N0} variant(s)";

    public string SelectedShaderSummaryText => SelectedShader is null
        ? "No shader selected."
        : $"Shader: {SelectedShader.Name} | Source: {SelectedShader.SourcePath}";

    public string SelectedPermutationSummaryText => SelectedPermutation is null
        ? "No variant selected."
        : $"Variant: {SelectedPermutation.Name} | Value: {SelectedPermutation.Value}";

    public string BaselineVariantDetailText => BaselinePermutation is null
        ? "No baseline variant."
        : $"Name: {BaselinePermutation.Name}\nValue: {BaselinePermutation.Value}\nActive: {BaselinePermutation.IsActive}";

    public string SelectedVariantDetailText => SelectedPermutation is null
        ? "No selected variant."
        : $"Name: {SelectedPermutation.Name}\nValue: {SelectedPermutation.Value}\nActive: {SelectedPermutation.IsActive}";

    public string ComparisonSummaryText { get; private set; } = "No comparison available.";

    public void LoadMaterial(MhMaterialInstance? material)
    {
        selectedMaterial = material;
        SelectedShader = material?.ShaderReference;
        StatusText = material is null
            ? "No material selected."
            : $"Loaded {Variants.Count:N0} shader variant(s) for comparison.";
    }

    public void ActivateSelectedVariant()
    {
        if (SelectedPermutation is null)
            return;

        variantService.ActivateVariant(Variants, SelectedPermutation);
        BaselinePermutation = variantService.ResolveBaseline(Variants);
        StatusText = $"Activated variant {SelectedPermutation.Name}.";
        RefreshComparisonSummary();
    }

    public string ExportVariantMetadata()
    {
        return JsonSerializer.Serialize(new ShaderVariantMetadata
        {
            ShaderName = SelectedShader?.Name ?? string.Empty,
            ShaderSourcePath = SelectedShader?.SourcePath ?? string.Empty,
            MaterialPath = selectedMaterial?.Path ?? string.Empty,
            Variants = Variants.ToList()
        }, JsonOptions);
    }

    private void RefreshComparisonSummary()
    {
        if (BaselinePermutation is null || SelectedPermutation is null)
        {
            ComparisonSummaryText = "No comparison available.";
            RaisePropertyChanged(nameof(ComparisonSummaryText));
            return;
        }

        ComparisonSummaryText = variantService.BuildComparisonSummary(BaselinePermutation, SelectedPermutation);
        RaisePropertyChanged(nameof(ComparisonSummaryText));
    }

    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true
    };

    private sealed class ShaderVariantMetadata
    {
        public string ShaderName { get; set; } = string.Empty;

        public string ShaderSourcePath { get; set; } = string.Empty;

        public string MaterialPath { get; set; } = string.Empty;

        public List<MhShaderPermutation> Variants { get; set; } = [];
    }
}

