using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialModels;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialServices;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialViewModels;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialOverrideTool;

public sealed class MaterialOverrideViewModel : MaterialToolViewModelBase
{
    private readonly MaterialOverrideService overrideService;
    private MhMaterialInstance? selectedMaterial;
    private MhMaterialOverrideProfile? selectedProfile;
    private string tintColorText = "#FFFFFFFF";
    private string roughnessScaleText = "1";
    private string emissiveScaleText = "1";

    public MaterialOverrideViewModel()
        : this(new MaterialCoreServices())
    {
    }

    public MaterialOverrideViewModel(MaterialCoreServices services)
    {
        overrideService = services.Overrides;
        Title = "Material Override Tool";
        Profiles = [];
        OverrideParameters = [];
    }

    public ObservableCollection<MhMaterialOverrideProfile> Profiles { get; }

    public ObservableCollection<EditableOverrideParameter> OverrideParameters { get; }

    public MhMaterialInstance? SelectedMaterial
    {
        get => selectedMaterial;
        set
        {
            if (!SetProperty(ref selectedMaterial, value))
                return;

            Profiles.Clear();
            OverrideParameters.Clear();
            if (selectedMaterial is null)
            {
                SelectedProfile = null;
                return;
            }

            IReadOnlyList<MhMaterialOverrideProfile> sourceProfiles = selectedMaterial.OverrideProfiles.Count == 0
                ? [overrideService.BuildDefaultProfile(selectedMaterial)]
                : overrideService.BuildProfiles(selectedMaterial.OverrideProfiles);

            foreach (MhMaterialOverrideProfile profile in sourceProfiles)
                Profiles.Add(profile);

            SelectedProfile = Profiles.FirstOrDefault();
            RaisePropertyChanged(nameof(SelectedMaterialSummaryText));
            RaisePropertyChanged(nameof(ProfileCountText));
        }
    }

    public MhMaterialOverrideProfile? SelectedProfile
    {
        get => selectedProfile;
        set
        {
            if (!SetProperty(ref selectedProfile, value))
                return;

            ReloadSelectedProfileBindings();
            RaisePropertyChanged(nameof(SelectedProfileSummaryText));
        }
    }

    public string TintColorText
    {
        get => tintColorText;
        set => SetProperty(ref tintColorText, value);
    }

    public string RoughnessScaleText
    {
        get => roughnessScaleText;
        set => SetProperty(ref roughnessScaleText, value);
    }

    public string EmissiveScaleText
    {
        get => emissiveScaleText;
        set => SetProperty(ref emissiveScaleText, value);
    }

    public string ProfileCountText => $"{Profiles.Count:N0} profile(s)";

    public string SelectedMaterialSummaryText => SelectedMaterial is null
        ? "No material selected."
        : $"Material: {SelectedMaterial.Name}";

    public string SelectedProfileSummaryText => SelectedProfile is null
        ? "No override profile selected."
        : $"Profile: {SelectedProfile.Name} | Tint: {SelectedProfile.TintColor}";

    public void AddFirstMissingMaterialParameter()
    {
        if (SelectedMaterial is null || SelectedProfile is null)
            return;

        MhMaterialParameter? parameter = SelectedMaterial.Parameters.FirstOrDefault(candidate =>
            !SelectedProfile.Overrides.Any(existing => string.Equals(existing.Name, candidate.Name, StringComparison.OrdinalIgnoreCase) &&
                                                      string.Equals(existing.Category, candidate.Category, StringComparison.OrdinalIgnoreCase)));

        if (parameter is null)
        {
            StatusText = "All material parameters are already in this override profile.";
            return;
        }

        MhMaterialParameter clone = overrideService.CloneParameter(parameter);
        SelectedProfile.Overrides.Add(clone);
        OverrideParameters.Add(new EditableOverrideParameter(clone.Name, clone.Category, overrideService.GetParameterValueText(clone)));
        StatusText = $"Added override parameter {clone.Name}.";
    }

    public void RemoveOverrideParameter(EditableOverrideParameter row)
    {
        if (SelectedProfile is null || row is null)
            return;

        MhMaterialParameter? parameter = SelectedProfile.Overrides.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, row.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.Category, row.Category, StringComparison.OrdinalIgnoreCase));
        if (parameter is null)
            return;

        SelectedProfile.Overrides.Remove(parameter);
        OverrideParameters.Remove(row);
        StatusText = $"Removed override parameter {row.Name}.";
    }

    public bool ApplyProfile()
    {
        if (SelectedMaterial is null || SelectedProfile is null)
            return false;

        if (!float.TryParse(RoughnessScaleText, NumberStyles.Float, CultureInfo.InvariantCulture, out float roughnessScale))
        {
            StatusText = "Roughness scale must be numeric.";
            return false;
        }

        if (!float.TryParse(EmissiveScaleText, NumberStyles.Float, CultureInfo.InvariantCulture, out float emissiveScale))
        {
            StatusText = "Emissive scale must be numeric.";
            return false;
        }

        SelectedProfile.TintColor = string.IsNullOrWhiteSpace(TintColorText) ? "#FFFFFFFF" : TintColorText.Trim();
        SelectedProfile.RoughnessScale = roughnessScale;
        SelectedProfile.EmissiveScale = emissiveScale;

        for (int i = 0; i < OverrideParameters.Count && i < SelectedProfile.Overrides.Count; i++)
        {
            EditableOverrideParameter row = OverrideParameters[i];
            MhMaterialParameter parameter = SelectedProfile.Overrides[i];
            if (!overrideService.TrySetParameterValueText(parameter, row.ValueText))
            {
                StatusText = $"Invalid override value for {row.Name}.";
                return false;
            }
        }

        overrideService.ApplyProfile(SelectedMaterial, SelectedProfile);
        StatusText = $"Applied override profile {SelectedProfile.Name}.";
        return true;
    }

    public void ResetProfile()
    {
        if (SelectedProfile is null)
            return;

        overrideService.ResetProfile(SelectedProfile);
        ReloadSelectedProfileBindings();
        StatusText = $"Reset override profile {SelectedProfile.Name}.";
    }

    public string ExportSelectedProfile()
    {
        if (SelectedProfile is null)
            return string.Empty;

        return JsonSerializer.Serialize(SelectedProfile, JsonOptions);
    }

    public bool ImportProfileJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;

        MhMaterialOverrideProfile? profile = JsonSerializer.Deserialize<MhMaterialOverrideProfile>(json, JsonOptions);
        if (profile is null)
            return false;

        Profiles.Add(profile);
        SelectedProfile = profile;
        StatusText = $"Imported override profile {profile.Name}.";
        return true;
    }

    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true
    };

    private void ReloadSelectedProfileBindings()
    {
        OverrideParameters.Clear();
        if (selectedProfile is null)
        {
            TintColorText = "#FFFFFFFF";
            RoughnessScaleText = "1";
            EmissiveScaleText = "1";
            return;
        }

        TintColorText = selectedProfile.TintColor;
        RoughnessScaleText = selectedProfile.RoughnessScale.ToString("0.###", CultureInfo.InvariantCulture);
        EmissiveScaleText = selectedProfile.EmissiveScale.ToString("0.###", CultureInfo.InvariantCulture);

        foreach (MhMaterialParameter parameter in selectedProfile.Overrides)
            OverrideParameters.Add(new EditableOverrideParameter(parameter.Name, parameter.Category, overrideService.GetParameterValueText(parameter)));
    }
}

public sealed class EditableOverrideParameter : Core.NotifyPropertyChangedBase
{
    private string valueText;

    public EditableOverrideParameter(string name, string category, string valueText)
    {
        Name = name;
        Category = category;
        this.valueText = valueText;
    }

    public string Name { get; }

    public string Category { get; }

    public string ValueText
    {
        get => valueText;
        set => SetProperty(ref valueText, value);
    }
}

