using System.Text;

namespace OmegaAssetStudio.Retargeting;

public static class BoneNameHeuristics
{
    private static readonly string[] Prefixes =
    [
        "bip01_",
        "b_",
        "bone_",
        "bn_",
        "chr_",
        "def_",
        "skel_",
        "mixamorig:",
        "valvebiped_",
        "g_",
        "joint_"
    ];

    public static string StripPrefixes(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        string normalized = name.Trim();
        string lower = normalized.ToLowerInvariant();
        foreach (string prefix in Prefixes)
        {
            if (lower.StartsWith(prefix, StringComparison.Ordinal))
                return normalized[prefix.Length..];
        }

        return normalized;
    }

    public static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        string stripped = StripPrefixes(name);
        StringBuilder builder = new(stripped.Length);
        foreach (char character in stripped)
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    public static string Canonicalize(string name)
    {
        string normalized = Normalize(name);
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        List<string> tokens = [];
        BoneSide side = GetSide(normalized);
        if (side == BoneSide.Left)
            tokens.Add("left");
        else if (side == BoneSide.Right)
            tokens.Add("right");

        BoneRegion region = GetRegion(normalized);
        if (region != BoneRegion.Unknown)
            tokens.Add(region.ToString().ToLowerInvariant());

        if (IsTwistBone(normalized))
            tokens.Add("twist");

        string digits = new(normalized.Where(char.IsDigit).ToArray());
        if (!string.IsNullOrWhiteSpace(digits))
            tokens.Add(digits);

        if (tokens.Count == 0)
            tokens.Add(normalized);

        return string.Join('_', tokens);
    }

    public static BoneSide GetSide(string name)
    {
        string normalized = Normalize(name);
        if (string.IsNullOrWhiteSpace(normalized))
            return BoneSide.Unknown;

        if (normalized.Contains("left", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("lft", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("_l", StringComparison.OrdinalIgnoreCase))
        {
            return BoneSide.Left;
        }

        if (normalized.Contains("right", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("rgt", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("_r", StringComparison.OrdinalIgnoreCase))
        {
            return BoneSide.Right;
        }

        return BoneSide.Unknown;
    }

    public static BoneRegion GetRegion(string name)
    {
        string normalized = Normalize(name);
        if (string.IsNullOrWhiteSpace(normalized))
            return BoneRegion.Unknown;

        return normalized switch
        {
            _ when ContainsAny(normalized, "root", "armature", "rootnode") => BoneRegion.Root,
            _ when ContainsAny(normalized, "pelvis", "hip", "hips", "crotch") => BoneRegion.Pelvis,
            _ when ContainsAny(normalized, "spine", "chest", "torso", "breast") => BoneRegion.Spine,
            _ when ContainsAny(normalized, "neck") => BoneRegion.Neck,
            _ when ContainsAny(normalized, "head", "jaw", "brow", "eyelid", "eye") => BoneRegion.Head,
            _ when ContainsAny(normalized, "clav", "shoulder") => BoneRegion.Shoulder,
            _ when ContainsAny(normalized, "upperarm", "uparm", "bicep") => BoneRegion.UpperArm,
            _ when ContainsAny(normalized, "forearm", "forarm", "lowerarm", "elbow") => BoneRegion.Forearm,
            _ when ContainsAny(normalized, "hand", "palm", "wrist") => BoneRegion.Hand,
            _ when ContainsAny(normalized, "thumb") => BoneRegion.Thumb,
            _ when ContainsAny(normalized, "index") => BoneRegion.IndexFinger,
            _ when ContainsAny(normalized, "middle") => BoneRegion.MiddleFinger,
            _ when ContainsAny(normalized, "ring") => BoneRegion.RingFinger,
            _ when ContainsAny(normalized, "pinky", "little") => BoneRegion.PinkyFinger,
            _ when ContainsAny(normalized, "thigh", "upleg", "upleftleg", "upleftthigh", "legupper") => BoneRegion.Thigh,
            _ when ContainsAny(normalized, "calf", "lowerleg", "leg", "knee") => BoneRegion.Calf,
            _ when ContainsAny(normalized, "foot", "ankle", "ball") => BoneRegion.Foot,
            _ when ContainsAny(normalized, "toe") => BoneRegion.Toe,
            _ when ContainsAny(normalized, "ik") => BoneRegion.Ik,
            _ when ContainsAny(normalized, "attach", "weapon", "throwable") => BoneRegion.Attachment,
            _ => BoneRegion.Unknown
        };
    }

    public static bool IsTwistBone(string name)
    {
        string normalized = Normalize(name);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        return ContainsAny(normalized, "twist", "twst", "roll");
    }

    public static bool IsHelperBone(string name)
    {
        string normalized = Normalize(name);
        if (string.IsNullOrWhiteSpace(normalized))
            return true;

        return ContainsAny(normalized, "offset", "helper", "attach", "ik", "twist", "armature", "nub", "marker");
    }

    public static float ScoreSimilarity(string source, string target)
    {
        string sourceCanonical = Canonicalize(source);
        string targetCanonical = Canonicalize(target);
        if (string.IsNullOrWhiteSpace(sourceCanonical) || string.IsNullOrWhiteSpace(targetCanonical))
            return 0.0f;

        if (string.Equals(sourceCanonical, targetCanonical, StringComparison.OrdinalIgnoreCase))
            return 1.0f;

        HashSet<string> sourceTokens = sourceCanonical.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> targetTokens = targetCanonical.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (sourceTokens.Count == 0 || targetTokens.Count == 0)
            return 0.0f;

        int overlap = sourceTokens.Intersect(targetTokens, StringComparer.OrdinalIgnoreCase).Count();
        int union = sourceTokens.Count + targetTokens.Count - overlap;
        if (union <= 0)
            return 0.0f;

        return overlap / (float)union;
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        foreach (string token in tokens)
        {
            if (value.Contains(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

public enum BoneSide
{
    Unknown,
    Left,
    Right
}

public enum BoneRegion
{
    Unknown,
    Root,
    Pelvis,
    Spine,
    Neck,
    Head,
    Shoulder,
    UpperArm,
    Forearm,
    Hand,
    Thumb,
    IndexFinger,
    MiddleFinger,
    RingFinger,
    PinkyFinger,
    Thigh,
    Calf,
    Foot,
    Toe,
    Ik,
    Attachment
}

