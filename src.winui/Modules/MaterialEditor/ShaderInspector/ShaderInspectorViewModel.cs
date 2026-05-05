using System.Collections.ObjectModel;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialModels;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialServices;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialViewModels;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.ShaderInspector;

public sealed class ShaderInspectorViewModel : MaterialToolViewModelBase
{
    private readonly ShaderService shaderService;
    private MhMaterialInstance? selectedMaterial;
    private MhShaderReference? selectedShader;
    private MhShaderPermutation? selectedPermutation;

    public ShaderInspectorViewModel()
        : this(new MaterialCoreServices())
    {
    }

    public ShaderInspectorViewModel(MaterialCoreServices services)
    {
        shaderService = services.Shaders;
        Title = "Shader Inspector";
        Permutations = [];
        ParameterBindings = [];
        UsagePaths = [];
    }

    public ObservableCollection<MhShaderPermutation> Permutations { get; }

    public ObservableCollection<string> ParameterBindings { get; }

    public ObservableCollection<string> UsagePaths { get; }

    public MhShaderReference? SelectedShader
    {
        get => selectedShader;
        set
        {
            if (!SetProperty(ref selectedShader, value))
                return;

            Permutations.Clear();
            if (selectedShader is null)
            {
                SelectedPermutation = null;
                RaisePropertyChanged(nameof(SelectedShaderSummaryText));
                RaisePropertyChanged(nameof(PermutationCountText));
                return;
            }

            foreach (MhShaderPermutation permutation in selectedShader.Permutations)
                Permutations.Add(permutation);

            SelectedPermutation = Permutations.FirstOrDefault();
            RaisePropertyChanged(nameof(SelectedShaderSummaryText));
            RaisePropertyChanged(nameof(PermutationCountText));
        }
    }

    public MhShaderPermutation? SelectedPermutation
    {
        get => selectedPermutation;
        set
        {
            if (!SetProperty(ref selectedPermutation, value))
                return;

            RaisePropertyChanged(nameof(SelectedPermutationSummaryText));
        }
    }

    public string PermutationCountText => $"{Permutations.Count:N0} permutation(s)";

    public string SelectedShaderSummaryText => SelectedShader is null
        ? "No shader selected."
        : $"Shader: {SelectedShader.Name} | Source: {SelectedShader.SourcePath}";

    public string SelectedPermutationSummaryText => SelectedPermutation is null
        ? "No permutation selected."
        : $"Permutation: {SelectedPermutation.Name} | Value: {SelectedPermutation.Value}";

    public string MetadataSummaryText => SelectedShader is null
        ? "No shader metadata available."
        : $"Source UPK: {SelectedShader.SourceUpkPath}";

    public void LoadMaterial(MhMaterialInstance? material)
    {
        selectedMaterial = material;
        SelectedShader = material?.ShaderReference;

        ParameterBindings.Clear();
        foreach (string binding in shaderService.BuildParameterBindings(material))
            ParameterBindings.Add(binding);

        UsagePaths.Clear();
        foreach (string path in shaderService.BuildUsagePaths(material))
            UsagePaths.Add(path);

        StatusText = material is null
            ? "No material selected."
            : $"{PermutationCountText}; {ParameterBindings.Count:N0} binding(s); {UsagePaths.Count:N0} usage path(s).";

        RaisePropertyChanged(nameof(MetadataSummaryText));
    }
}

