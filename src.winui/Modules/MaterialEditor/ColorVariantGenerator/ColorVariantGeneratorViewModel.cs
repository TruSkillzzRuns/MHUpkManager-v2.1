using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml.Media;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialModels;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialServices;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialViewModels;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.ColorVariantGenerator;

public sealed class ColorVariantGeneratorViewModel : MaterialToolViewModelBase
{
    private sealed record VariantSnapshot(
        List<MhColorVariantProfile> Variants,
        int SelectedIndex,
        List<MhColorVariantProfile> MaterialVariants);

    private readonly ColorVariantService colorVariantService;
    private readonly Stack<VariantSnapshot> undoStack = new();
    private MhMaterialInstance? selectedMaterial;
    private MhColorVariantProfile? selectedVariant;
    private string hueShiftText = "0";
    private string saturationScaleText = "1";
    private string valueScaleText = "1";

    public ColorVariantGeneratorViewModel()
        : this(new MaterialCoreServices())
    {
    }

    public ColorVariantGeneratorViewModel(MaterialCoreServices services)
    {
        colorVariantService = services.ColorVariants;
        Title = "Color Variant Generator";
        Variants = [];
        Presets = new ObservableCollection<string>(["Warm", "Cool", "Mono"]);
    }

    public ObservableCollection<MhColorVariantProfile> Variants { get; }

    public ObservableCollection<string> Presets { get; }

    public MhMaterialInstance? SelectedMaterial
    {
        get => selectedMaterial;
        set
        {
            if (!SetProperty(ref selectedMaterial, value))
                return;

            undoStack.Clear();
            RaisePropertyChanged(nameof(CanRevert));
            Variants.Clear();
            if (selectedMaterial is null)
            {
                SelectedVariant = null;
                return;
            }

            IReadOnlyList<MhColorVariantProfile> source = selectedMaterial.ColorVariants.Count == 0
                ? colorVariantService.BuildPresetVariants(selectedMaterial.Name)
                : colorVariantService.BuildVariants(selectedMaterial.ColorVariants);

            foreach (MhColorVariantProfile variant in source)
                Variants.Add(variant);

            SelectedVariant = Variants.FirstOrDefault();
            RaisePropertyChanged(nameof(SelectedMaterialSummaryText));
            RaisePropertyChanged(nameof(VariantCountText));
        }
    }

    public MhColorVariantProfile? SelectedVariant
    {
        get => selectedVariant;
        set
        {
            if (!SetProperty(ref selectedVariant, value))
                return;

            RaisePropertyChanged(nameof(SelectedVariantSummaryText));
            RaisePropertyChanged(nameof(PrimaryBrush));
            RaisePropertyChanged(nameof(SecondaryBrush));
            RaisePropertyChanged(nameof(AccentBrush));
        }
    }

    public string HueShiftText
    {
        get => hueShiftText;
        set => SetProperty(ref hueShiftText, value);
    }

    public string SaturationScaleText
    {
        get => saturationScaleText;
        set => SetProperty(ref saturationScaleText, value);
    }

    public string ValueScaleText
    {
        get => valueScaleText;
        set => SetProperty(ref valueScaleText, value);
    }

    public string VariantCountText => $"{Variants.Count:N0} variant(s)";

    public bool CanRevert => undoStack.Count > 0;

    public string SelectedMaterialSummaryText => SelectedMaterial is null
        ? "No material selected."
        : $"Material: {SelectedMaterial.Name}";

    public string SelectedVariantSummaryText => SelectedVariant is null
        ? "No color variant selected."
        : $"Variant: {SelectedVariant.Name} | Primary: {SelectedVariant.PrimaryColor}";

    public SolidColorBrush PrimaryBrush => BuildBrush(SelectedVariant?.PrimaryColor);

    public SolidColorBrush SecondaryBrush => BuildBrush(SelectedVariant?.SecondaryColor);

    public SolidColorBrush AccentBrush => BuildBrush(SelectedVariant?.AccentColor);

    public void ApplyPreset(string presetName)
    {
        if (SelectedMaterial is null || string.IsNullOrWhiteSpace(presetName))
            return;

        MhColorVariantProfile? variant = colorVariantService.BuildPresetVariants(SelectedMaterial.Name)
            .FirstOrDefault(candidate => candidate.Name.Contains(presetName, StringComparison.OrdinalIgnoreCase));
        if (variant is null)
            return;

        CaptureSnapshot();
        Variants.Add(variant);
        SelectedVariant = variant;
        StatusText = $"Preset {presetName} added.";
        RaisePropertyChanged(nameof(VariantCountText));
    }

    public bool GenerateVariantFromHsv()
    {
        if (SelectedMaterial is null || SelectedVariant is null)
            return false;

        if (!float.TryParse(HueShiftText, NumberStyles.Float, CultureInfo.InvariantCulture, out float hueShift))
        {
            StatusText = "Hue shift must be numeric.";
            return false;
        }

        if (!float.TryParse(SaturationScaleText, NumberStyles.Float, CultureInfo.InvariantCulture, out float saturationScale))
        {
            StatusText = "Saturation scale must be numeric.";
            return false;
        }

        if (!float.TryParse(ValueScaleText, NumberStyles.Float, CultureInfo.InvariantCulture, out float valueScale))
        {
            StatusText = "Value scale must be numeric.";
            return false;
        }

        MhColorVariantProfile generated = colorVariantService.BuildHsvVariant(
            $"{SelectedVariant.Name} HSV",
            SelectedVariant.PrimaryColor,
            SelectedVariant.SecondaryColor,
            SelectedVariant.AccentColor,
            hueShift,
            saturationScale,
            valueScale);
        CaptureSnapshot();
        Variants.Add(generated);
        SelectedVariant = generated;
        StatusText = $"Generated HSV variant {generated.Name}.";
        RaisePropertyChanged(nameof(VariantCountText));
        return true;
    }

    public void ApplySelectedVariantToMaterial()
    {
        if (SelectedMaterial is null || SelectedVariant is null)
            return;

        CaptureSnapshot();
        colorVariantService.ApplyVariant(SelectedMaterial, SelectedVariant);
        StatusText = $"Applied color variant {SelectedVariant.Name}.";
    }

    public string ExportVariants()
    {
        return JsonSerializer.Serialize(Variants.ToList(), JsonOptions);
    }

    public bool ImportVariants(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;

        List<MhColorVariantProfile>? variants = JsonSerializer.Deserialize<List<MhColorVariantProfile>>(json, JsonOptions);
        if (variants is null)
            return false;

        CaptureSnapshot();
        foreach (MhColorVariantProfile variant in variants)
            Variants.Add(variant);

        SelectedVariant = Variants.LastOrDefault();
        StatusText = $"Imported {variants.Count:N0} variant(s).";
        RaisePropertyChanged(nameof(VariantCountText));
        return true;
    }

    public void RevertLastChange()
    {
        if (undoStack.Count == 0)
            return;

        VariantSnapshot snapshot = undoStack.Pop();
        Variants.Clear();
        foreach (MhColorVariantProfile variant in snapshot.Variants)
            Variants.Add(CloneVariant(variant));

        if (SelectedMaterial is not null)
            SelectedMaterial.ColorVariants = snapshot.MaterialVariants.Select(CloneVariant).ToList();

        if (snapshot.SelectedIndex >= 0 && snapshot.SelectedIndex < Variants.Count)
            SelectedVariant = Variants[snapshot.SelectedIndex];
        else
            SelectedVariant = Variants.FirstOrDefault();

        RaisePropertyChanged(nameof(CanRevert));
        RaisePropertyChanged(nameof(VariantCountText));
        StatusText = "Reverted last color variant change.";
    }

    private void CaptureSnapshot()
    {
        List<MhColorVariantProfile> currentVariants = Variants.Select(CloneVariant).ToList();
        int selectedIndex = selectedVariant is null ? -1 : Variants.IndexOf(selectedVariant);
        List<MhColorVariantProfile> materialVariants = SelectedMaterial?.ColorVariants.Select(CloneVariant).ToList() ?? [];
        undoStack.Push(new VariantSnapshot(currentVariants, selectedIndex, materialVariants));
        RaisePropertyChanged(nameof(CanRevert));
    }

    private static MhColorVariantProfile CloneVariant(MhColorVariantProfile source)
    {
        return new MhColorVariantProfile
        {
            Name = source.Name,
            PrimaryColor = source.PrimaryColor,
            SecondaryColor = source.SecondaryColor,
            AccentColor = source.AccentColor,
            Notes = source.Notes
        };
    }

    private static SolidColorBrush BuildBrush(string? colorText)
    {
        if (!ColorVariantService.TryNormalizeHex(colorText ?? string.Empty, out string normalized))
            normalized = "#FFFFFFFF";

        byte a = byte.Parse(normalized.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte r = byte.Parse(normalized.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte g = byte.Parse(normalized.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte b = byte.Parse(normalized.AsSpan(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));
    }

    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true
    };
}

