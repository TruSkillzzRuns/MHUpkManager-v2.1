using System.Text.Json;
using System.Text.Json.Serialization;

namespace OmegaAssetStudio.Retargeting;

public sealed record RetargetMappingProfile
{
    public string ProfileName { get; init; } = string.Empty;
    public string SourceSkeletonPath { get; init; } = string.Empty;
    public string TargetSkeletonPath { get; init; } = string.Empty;
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public Dictionary<string, string> Mappings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ManualOverrides { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class MappingProfileManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ProfilesRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OmegaAssetStudio",
        "Profiles",
        "Retargeting");

    public string GetProfilePath(string profileName)
    {
        string safeName = SanitizeProfileName(profileName);
        if (string.IsNullOrWhiteSpace(safeName))
            throw new InvalidOperationException("A retargeting profile name is required.");

        return Path.Combine(ProfilesRoot, $"{safeName}.json");
    }

    public void SaveProfile(RetargetMappingProfile profile)
    {
        if (profile is null)
            throw new ArgumentNullException(nameof(profile));

        Directory.CreateDirectory(ProfilesRoot);
        string path = GetProfilePath(profile.ProfileName);
        string json = JsonSerializer.Serialize(profile, JsonOptions);
        File.WriteAllText(path, json);
    }

    public bool TryLoadProfile(string profileName, out RetargetMappingProfile? profile, out string path, out string error)
    {
        profile = null;
        path = string.Empty;
        error = string.Empty;

        try
        {
            path = GetProfilePath(profileName);
            if (!File.Exists(path))
            {
                error = $"Mapping profile '{profileName}' was not found.";
                return false;
            }

            string json = File.ReadAllText(path);
            profile = JsonSerializer.Deserialize<RetargetMappingProfile>(json, JsonOptions);
            if (profile is null)
            {
                error = $"Mapping profile '{profileName}' could not be parsed.";
                return false;
            }

            profile = profile with { ProfileName = string.IsNullOrWhiteSpace(profile.ProfileName) ? profileName : profile.ProfileName };
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public IReadOnlyList<string> ListProfiles()
    {
        if (!Directory.Exists(ProfilesRoot))
            return [];

        return Directory.EnumerateFiles(ProfilesRoot, "*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string SanitizeProfileName(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
            return string.Empty;

        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitized = new(profileName.Trim()
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());

        while (sanitized.Contains("..", StringComparison.Ordinal))
            sanitized = sanitized.Replace("..", ".", StringComparison.Ordinal);

        return sanitized.Trim('.', ' ', '\t', '\r', '\n');
    }
}

