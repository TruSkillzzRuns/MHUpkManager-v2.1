using System.Collections.Generic;
using System.IO;
using System.Linq;
using OmegaAssetStudio.ThanosMigration.Models;

namespace OmegaAssetStudio.ThanosMigration.Services;

public sealed class ThanosPrototypeMergePlanner
{
    public IReadOnlyList<ThanosPrototypeMergePlan> BuildMergePlans(IReadOnlyList<ThanosPrototypeSource> sources, string client152Root)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(client152Root);

        string fullRoot = Path.GetFullPath(client152Root);
        Directory.CreateDirectory(fullRoot);
        Dictionary<string, string> targetUpksByName = IndexTargetUpks(fullRoot);

        string raidTargetPath = Path.Combine(fullRoot, "MHGameContent_Raid_Thanos_152.upk");
        string genericTargetPath = Path.Combine(fullRoot, "Thanos_MergedContent_152.upk");

        List<ThanosPrototypeMergePlan> plans = [];

        foreach (var group in sources.GroupBy(source => ResolveTargetPath(source, fullRoot, raidTargetPath, genericTargetPath, targetUpksByName), StringComparer.OrdinalIgnoreCase))
        {
            plans.Add(new ThanosPrototypeMergePlan
            {
                TargetUpkPath = group.Key,
                SourcePrototypes = group.ToArray(),
                Notes = $"Sources={group.Count():N0}"
            });
        }

        return plans
            .OrderBy(plan => plan.TargetUpkPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveTargetPath(
        ThanosPrototypeSource source,
        string client152Root,
        string raidTargetPath,
        string genericTargetPath,
        IReadOnlyDictionary<string, string> targetUpksByName)
    {
        if (source.IsRequiredRaidRoot)
            return raidTargetPath;

        string packageName = string.IsNullOrWhiteSpace(source.Dependency.PackageName)
            ? Path.GetFileNameWithoutExtension(source.SourceUpkPath)
            : source.Dependency.PackageName;

        string normalizedName = NormalizePackageName(packageName);
        if (targetUpksByName.TryGetValue(normalizedName, out string? matched))
            return matched;

        if (packageName.Contains("raid", StringComparison.OrdinalIgnoreCase) ||
            packageName.Contains("thanos", StringComparison.OrdinalIgnoreCase) ||
            source.ExportPathLikeRaidHint())
        {
            return raidTargetPath;
        }

        return genericTargetPath;
    }

    private static Dictionary<string, string> IndexTargetUpks(string client152Root)
    {
        Dictionary<string, string> lookup = new(StringComparer.OrdinalIgnoreCase);

        foreach (string path in Directory.EnumerateFiles(client152Root, "*.upk", SearchOption.AllDirectories))
        {
            string name = NormalizePackageName(Path.GetFileName(path));
            if (!lookup.ContainsKey(name))
                lookup[name] = path;
        }

        return lookup;
    }

    private static string NormalizePackageName(string packageName)
    {
        string file = Path.GetFileName(packageName);
        return file.EndsWith(".upk", StringComparison.OrdinalIgnoreCase)
            ? file[..^4]
            : file;
    }
}

internal static class ThanosPrototypeMergePlannerExtensions
{
    public static bool ExportPathLikeRaidHint(this ThanosPrototypeSource source)
    {
        string haystack = string.Join(" ", new[]
        {
            source.Dependency.Name,
            source.Dependency.ClassName,
            source.Dependency.OuterName,
            source.ExportObjectName,
            source.ExportClassName,
            source.ExportOuterName
        }.Where(item => !string.IsNullOrWhiteSpace(item)));

        return haystack.Contains("raid", StringComparison.OrdinalIgnoreCase) ||
               haystack.Contains("thanos", StringComparison.OrdinalIgnoreCase);
    }
}

