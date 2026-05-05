using System.IO;
using System.Text.Json;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration;

internal static class PrototypeMergerSessionStore
{
    private static readonly string StoragePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmegaAssetStudio", "PrototypeMergerSession.json");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static PrototypeMergerSessionData Load()
    {
        try
        {
            if (!File.Exists(StoragePath))
                return new PrototypeMergerSessionData();

            PrototypeMergerSessionData? data = JsonSerializer.Deserialize<PrototypeMergerSessionData>(File.ReadAllText(StoragePath), JsonOptions);
            return data ?? new PrototypeMergerSessionData();
        }
        catch
        {
            return new PrototypeMergerSessionData();
        }
    }

    public static void Save(PrototypeMergerSessionData data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);
            File.WriteAllText(StoragePath, JsonSerializer.Serialize(data, JsonOptions));
        }
        catch
        {
        }
    }

    public static void Remember(string? client148Root = null, string? client152Root = null, string? sourceUpkPath = null)
    {
        PrototypeMergerSessionData data = Load();

        if (client148Root is not null)
            data.Client148Root = client148Root;

        if (client152Root is not null)
            data.Client152Root = client152Root;

        if (sourceUpkPath is not null)
            data.SourceUpkPath = sourceUpkPath;

        Save(data);
    }

    public sealed class PrototypeMergerSessionData
    {
        public string Client148Root { get; set; } = string.Empty;

        public string Client152Root { get; set; } = string.Empty;

        public string SourceUpkPath { get; set; } = string.Empty;
    }
}

