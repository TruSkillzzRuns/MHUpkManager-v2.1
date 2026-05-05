using System.Globalization;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialModels;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialServices;

public sealed class ColorVariantService
{
    public IReadOnlyList<MhColorVariantProfile> BuildVariants(IEnumerable<MhColorVariantProfile> variants)
    {
        ArgumentNullException.ThrowIfNull(variants);
        return variants.ToArray();
    }

    public IReadOnlyList<MhColorVariantProfile> BuildPresetVariants(string materialName)
    {
        return
        [
            new MhColorVariantProfile
            {
                Name = $"{materialName} Warm",
                PrimaryColor = "#FFDC7D4A",
                SecondaryColor = "#FF7A2E1D",
                AccentColor = "#FFFFD27A",
                Notes = "Warm preset"
            },
            new MhColorVariantProfile
            {
                Name = $"{materialName} Cool",
                PrimaryColor = "#FF4A8ADC",
                SecondaryColor = "#FF1D2E7A",
                AccentColor = "#FFA4E0FF",
                Notes = "Cool preset"
            },
            new MhColorVariantProfile
            {
                Name = $"{materialName} Mono",
                PrimaryColor = "#FFCFCFCF",
                SecondaryColor = "#FF8A8A8A",
                AccentColor = "#FFFFFFFF",
                Notes = "Monochrome preset"
            }
        ];
    }

    public MhColorVariantProfile BuildHsvVariant(string name, string basePrimary, string baseSecondary, string baseAccent, float hueDegrees, float saturationScale, float valueScale)
    {
        string primary = TransformHexColor(basePrimary, hueDegrees, saturationScale, valueScale);
        string secondary = TransformHexColor(baseSecondary, hueDegrees, saturationScale, valueScale);
        string accent = TransformHexColor(baseAccent, hueDegrees, saturationScale, valueScale);
        return new MhColorVariantProfile
        {
            Name = name,
            PrimaryColor = primary,
            SecondaryColor = secondary,
            AccentColor = accent,
            Notes = $"HSV shift H:{hueDegrees:0.#} S:{saturationScale:0.##} V:{valueScale:0.##}"
        };
    }

    public void ApplyVariant(MhMaterialInstance material, MhColorVariantProfile variant)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(variant);

        string[] colors = [variant.PrimaryColor, variant.SecondaryColor, variant.AccentColor];
        int index = 0;
        foreach (MhVectorParameter vector in material.Parameters.OfType<MhVectorParameter>())
        {
            if (!(vector.Name.Contains("color", StringComparison.OrdinalIgnoreCase) || vector.Name.Contains("tint", StringComparison.OrdinalIgnoreCase)))
                continue;

            vector.Value = colors[index % colors.Length];
            index++;
        }
    }

    public static bool TryNormalizeHex(string value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string raw = value.Trim();
        if (raw.StartsWith('#'))
            raw = raw[1..];

        if (raw.Length == 6)
            raw = $"FF{raw}";

        if (raw.Length != 8 || !uint.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            return false;

        normalized = $"#{raw.ToUpperInvariant()}";
        return true;
    }

    private static string TransformHexColor(string hexColor, float hueDegrees, float saturationScale, float valueScale)
    {
        if (!TryNormalizeHex(hexColor, out string normalized))
            normalized = "#FFFFFFFF";

        byte a = byte.Parse(normalized.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte r = byte.Parse(normalized.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte g = byte.Parse(normalized.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte b = byte.Parse(normalized.AsSpan(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        RgbToHsv(r, g, b, out float h, out float s, out float v);
        h = (h + hueDegrees) % 360f;
        if (h < 0f)
            h += 360f;
        s = Math.Clamp(s * saturationScale, 0f, 1f);
        v = Math.Clamp(v * valueScale, 0f, 1f);

        (byte nr, byte ng, byte nb) = HsvToRgb(h, s, v);
        return $"#{a:X2}{nr:X2}{ng:X2}{nb:X2}";
    }

    private static void RgbToHsv(byte rByte, byte gByte, byte bByte, out float h, out float s, out float v)
    {
        float r = rByte / 255f;
        float g = gByte / 255f;
        float b = bByte / 255f;

        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        float delta = max - min;

        h = 0f;
        if (delta > 0f)
        {
            if (max == r)
                h = 60f * (((g - b) / delta) % 6f);
            else if (max == g)
                h = 60f * (((b - r) / delta) + 2f);
            else
                h = 60f * (((r - g) / delta) + 4f);
        }

        if (h < 0f)
            h += 360f;

        s = max == 0f ? 0f : delta / max;
        v = max;
    }

    private static (byte R, byte G, byte B) HsvToRgb(float h, float s, float v)
    {
        float c = v * s;
        float x = c * (1f - Math.Abs(((h / 60f) % 2f) - 1f));
        float m = v - c;

        float rp;
        float gp;
        float bp;

        if (h < 60f)
        {
            rp = c;
            gp = x;
            bp = 0f;
        }
        else if (h < 120f)
        {
            rp = x;
            gp = c;
            bp = 0f;
        }
        else if (h < 180f)
        {
            rp = 0f;
            gp = c;
            bp = x;
        }
        else if (h < 240f)
        {
            rp = 0f;
            gp = x;
            bp = c;
        }
        else if (h < 300f)
        {
            rp = x;
            gp = 0f;
            bp = c;
        }
        else
        {
            rp = c;
            gp = 0f;
            bp = x;
        }

        byte r = (byte)Math.Clamp((int)Math.Round((rp + m) * 255f), 0, 255);
        byte g = (byte)Math.Clamp((int)Math.Round((gp + m) * 255f), 0, 255);
        byte b = (byte)Math.Clamp((int)Math.Round((bp + m) * 255f), 0, 255);
        return (r, g, b);
    }
}
