using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OmegaAssetStudio.ThanosMigration.Models;
using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;

namespace OmegaAssetStudio.ThanosMigration.Services;

public sealed class ThanosPrototypeDiscoveryService
{
    private static readonly string[] RequiredRaidRootNames =
    [
        "ThanosRaidRegionBand",
        "ThanosRaidRegionBase",
        "ThanosRaidRegionGreen"
    ];

    private readonly UpkFileRepository repository = new();

    public async Task<IReadOnlyList<ThanosPrototypeSource>> FindPrototypeSources(
        ThanosDependencyReport report,
        string client148Root,
        IProgress<ThanosDiscoveryProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(client148Root);

        string fullRoot = Path.GetFullPath(client148Root);
        if (!Directory.Exists(fullRoot))
            return [];

        List<string> scanRoots = BuildScanRoots(fullRoot);
        List<string> upkPaths = scanRoots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.upk", SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Dictionary<string, List<string>> packageIndex = BuildPackageIndex(upkPaths);
        Dictionary<string, UnrealHeader> cache = new(StringComparer.OrdinalIgnoreCase);
        List<ThanosPrototypeSource> results = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        int processed = 0;
        List<ThanosDependencyItem> dependencies = BuildRequiredDependencySet(report.MissingDependencies);
        int total = dependencies.Count;

        foreach (ThanosDependencyItem dependency in dependencies)
        {
            processed++;
            progress?.Report(new ThanosDiscoveryProgress
            {
                CurrentItem = dependency.ObjectPath.Length > 0 ? dependency.ObjectPath : dependency.Name,
                CurrentFile = string.Empty,
                ProcessedItems = processed - 1,
                TotalItems = total,
                Status = $"Scanning dependency {processed:N0} of {total:N0}."
            });

            IReadOnlyList<string> candidatePaths = GetCandidateUpkPaths(dependency, upkPaths, packageIndex);
            ThanosPrototypeSource? bestSource = null;

            foreach (string upkPath in candidatePaths)
            {
                progress?.Report(new ThanosDiscoveryProgress
                {
                    CurrentItem = dependency.ObjectPath.Length > 0 ? dependency.ObjectPath : dependency.Name,
                    CurrentFile = upkPath,
                    ProcessedItems = processed - 1,
                    TotalItems = total,
                    Status = $"Scanning {Path.GetFileName(upkPath)} for prototype matches."
                });

                UnrealHeader header = await LoadHeaderAsync(upkPath, cache).ConfigureAwait(false);
                foreach (UnrealExportTableEntry export in header.ExportTable)
                {
                    PrototypeMatch match = ScoreMatch(dependency, header, upkPath, export);
                    if (match.Score <= 0)
                        continue;

                    if (bestSource is null || match.Score > bestSource.MatchScore)
                    {
                        bool isRequiredRaidRoot = IsRequiredRaidRootDependency(dependency);
                        bestSource = new ThanosPrototypeSource
                        {
                            Dependency = dependency,
                            SourceUpkPath = upkPath,
                            ExportIndex = export.TableIndex,
                            ExportObjectName = export.ObjectNameIndex?.Name ?? string.Empty,
                            ExportClassName = export.ClassReferenceNameIndex?.Name ?? string.Empty,
                            ExportOuterName = export.OuterReferenceNameIndex?.Name ?? string.Empty,
                            MatchScore = match.Score,
                            MatchReason = match.Reason,
                            IsRaidRelevant = IsRaidRelevant(dependency, upkPath),
                            RaidReason = GetRaidReason(dependency, upkPath),
                            IsRequiredRaidRoot = isRequiredRaidRoot
                        };
                    }
                }
            }

            if (bestSource is null)
                continue;

            int minimumScore = bestSource.IsRequiredRaidRoot ? 55 : 80;
            if (bestSource.MatchScore < minimumScore)
                continue;

            if (!bestSource.IsRaidRelevant)
                continue;

            string key = $"{bestSource.SourceUpkPath}|{bestSource.ExportIndex}";
            if (!seen.Add(key))
                continue;

            results.Add(bestSource);

            progress?.Report(new ThanosDiscoveryProgress
            {
                CurrentItem = dependency.ObjectPath.Length > 0 ? dependency.ObjectPath : dependency.Name,
                CurrentFile = bestSource.SourceUpkPath,
                ProcessedItems = processed,
                TotalItems = total,
                Status = $"Matched {bestSource.ExportObjectName} in {Path.GetFileName(bestSource.SourceUpkPath)}."
            });

            await Task.Yield();
        }

        return results
            .OrderByDescending(source => source.MatchScore)
            .ThenBy(source => source.Dependency.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static List<string> BuildScanRoots(string selectedRoot)
    {
        HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);

        void addRoot(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                return;
            }

            if (Directory.Exists(fullPath))
                roots.Add(fullPath);
        }

        addRoot(selectedRoot);

        DirectoryInfo current = new(selectedRoot);
        if (current.Name.Equals("CookedPCConsole", StringComparison.OrdinalIgnoreCase))
        {
            addRoot(current.Parent?.FullName);

            DirectoryInfo? unrealEngineRoot = current.Parent?.Parent;
            if (unrealEngineRoot is not null)
            {
                addRoot(Path.Combine(unrealEngineRoot.FullName, "MarvelGame"));
                addRoot(Path.Combine(unrealEngineRoot.FullName, "MarvelGame", "CookedPCConsole"));
                addRoot(Path.Combine(unrealEngineRoot.FullName, "Engine"));
                addRoot(Path.Combine(unrealEngineRoot.FullName, "Engine", "CookedPCConsole"));
            }
        }
        else if (current.Name.Equals("MarvelGame", StringComparison.OrdinalIgnoreCase))
        {
            addRoot(Path.Combine(current.FullName, "CookedPCConsole"));
            addRoot(current.Parent?.FullName);
            if (current.Parent is not null)
                addRoot(Path.Combine(current.Parent.FullName, "Engine"));
        }

        return roots.ToList();
    }

    private static Dictionary<string, List<string>> BuildPackageIndex(IReadOnlyList<string> upkPaths)
    {
        Dictionary<string, List<string>> packageIndex = new(StringComparer.OrdinalIgnoreCase);
        foreach (string upkPath in upkPaths)
        {
            string fileName = Path.GetFileNameWithoutExtension(upkPath);
            if (!packageIndex.TryGetValue(fileName, out List<string>? bucket))
            {
                bucket = [];
                packageIndex[fileName] = bucket;
            }

            bucket.Add(upkPath);
        }

        return packageIndex;
    }

    private static IReadOnlyList<string> GetCandidateUpkPaths(
        ThanosDependencyItem dependency,
        IReadOnlyList<string> allUpkPaths,
        Dictionary<string, List<string>> packageIndex)
    {
        List<string> candidates = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        void addPaths(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                if (seen.Add(path))
                    candidates.Add(path);
            }
        }

        if (!string.IsNullOrWhiteSpace(dependency.PackageName))
        {
            string packageKey = dependency.PackageName.Trim();
            if (packageIndex.TryGetValue(packageKey, out List<string>? exactPackageMatches))
                addPaths(exactPackageMatches);

            addPaths(allUpkPaths.Where(path =>
                Path.GetFileNameWithoutExtension(path).Contains(packageKey, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(dependency.ObjectPath))
        {
            string[] segments = dependency.ObjectPath.Split(['.', '/', '\\', '_'], StringSplitOptions.RemoveEmptyEntries);
            foreach (string segment in segments.Where(segment => segment.Length >= 4))
            {
                addPaths(allUpkPaths.Where(path =>
                    Path.GetFileNameWithoutExtension(path).Contains(segment, StringComparison.OrdinalIgnoreCase)));

                if (candidates.Count >= 12)
                    break;
            }
        }

        if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(dependency.Name))
        {
            addPaths(allUpkPaths.Where(path =>
                Path.GetFileNameWithoutExtension(path).Contains(dependency.Name, StringComparison.OrdinalIgnoreCase)));
        }

        if (candidates.Count == 0)
            addPaths(allUpkPaths);

        return candidates;
    }

    private async Task<UnrealHeader> LoadHeaderAsync(string upkPath, Dictionary<string, UnrealHeader> cache)
    {
        if (cache.TryGetValue(upkPath, out UnrealHeader? cached))
            return cached;

        UnrealHeader header = await repository.LoadUpkFile(upkPath).ConfigureAwait(false);
        await header.ReadTablesAsync(null).ConfigureAwait(false);
        cache[upkPath] = header;
        return header;
    }

    private static List<ThanosDependencyItem> BuildRequiredDependencySet(IEnumerable<ThanosDependencyItem> items)
    {
        List<ThanosDependencyItem> filtered = items
            .Where(item => item.MissingInClient)
            .ToList();

        HashSet<string> seen = filtered
            .Select(BuildDependencyIdentity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool hasThanosSignal = filtered.Any(item =>
            BuildDependencyIdentity(item).Contains("thanos", StringComparison.OrdinalIgnoreCase) ||
            BuildDependencyIdentity(item).Contains("knowhere", StringComparison.OrdinalIgnoreCase) ||
            BuildDependencyIdentity(item).Contains("raid", StringComparison.OrdinalIgnoreCase));

        if (!hasThanosSignal)
            return filtered;

        foreach (string rootName in RequiredRaidRootNames)
        {
            ThanosDependencyItem synthetic = new()
            {
                Name = rootName,
                ObjectPath = $"Regions/RAIDS/ThanosRaid/{rootName}.prototype",
                PackageName = "Calligraphy",
                ClassName = "RegionPrototype",
                OuterName = "ThanosRaid",
                ReferenceKind = "RequiredRaidRoot",
                MissingInClient = true,
                Details = "Forced Thanos raid root prototype."
            };

            if (seen.Add(BuildDependencyIdentity(synthetic)))
                filtered.Add(synthetic);
        }

        return filtered;
    }

    private static string BuildDependencyIdentity(ThanosDependencyItem item)
    {
        return $"{item.ObjectPath}|{item.Name}|{item.PackageName}|{item.ClassName}|{item.ReferenceKind}";
    }

    private static bool IsRequiredRaidRootDependency(ThanosDependencyItem dependency)
    {
        if (dependency.ReferenceKind.Equals("RequiredRaidRoot", StringComparison.OrdinalIgnoreCase))
            return true;

        string haystack = $"{dependency.Name}|{dependency.ObjectPath}".ToLowerInvariant();
        return RequiredRaidRootNames.Any(root => haystack.Contains(root.ToLowerInvariant(), StringComparison.Ordinal));
    }

    private static PrototypeMatch ScoreMatch(ThanosDependencyItem dependency, UnrealHeader header, string upkPath, UnrealExportTableEntry export)
    {
        string packageName = Path.GetFileNameWithoutExtension(upkPath);
        string exportPath = export.GetPathName();
        string className = export.ClassReferenceNameIndex?.Name ?? string.Empty;
        string outerName = export.OuterReferenceNameIndex?.Name ?? string.Empty;
        string objectName = export.ObjectNameIndex?.Name ?? string.Empty;
        string dependencyPath = dependency.ObjectPath ?? dependency.Details ?? string.Empty;

        int score = 0;
        List<string> reasons = [];

        if (MatchesExact(dependencyPath, exportPath))
        {
            score += 150;
            reasons.Add("path");
        }
        else if (MatchesContains(dependencyPath, exportPath, header.Filename))
        {
            score += 60;
            reasons.Add("path~");
        }

        if (MatchesExact(dependency.Name, objectName))
        {
            score += 100;
            reasons.Add("name");
        }
        else if (MatchesContains(dependency.Name, objectName, exportPath))
        {
            score += 40;
            reasons.Add("name~");
        }

        if (MatchesExact(dependency.ClassName, className))
        {
            score += 90;
            reasons.Add("class");
        }
        else if (MatchesContains(dependency.ClassName, className, exportPath))
        {
            score += 30;
            reasons.Add("class~");
        }

        if (MatchesExact(dependency.OuterName, outerName))
        {
            score += 80;
            reasons.Add("outer");
        }
        else if (MatchesContains(dependency.OuterName, outerName, exportPath))
        {
            score += 20;
            reasons.Add("outer~");
        }

        if (MatchesExact(dependency.PackageName, packageName, header.Filename))
        {
            score += 70;
            reasons.Add("package");
        }
        else if (MatchesContains(dependency.PackageName, packageName, header.Filename))
        {
            score += 10;
            reasons.Add("package~");
        }

        return new PrototypeMatch
        {
            Score = score,
            Reason = reasons.Count == 0 ? string.Empty : string.Join(", ", reasons.Distinct(StringComparer.OrdinalIgnoreCase))
        };
    }

    private static bool MatchesExact(string? candidate, params string?[] values)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        string normalizedCandidate = Normalize(candidate);
        return values.Any(value => !string.IsNullOrWhiteSpace(value) &&
                                   string.Equals(Normalize(value), normalizedCandidate, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesContains(string? candidate, params string?[] values)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        string normalizedCandidate = Normalize(candidate);
        return values.Any(value => !string.IsNullOrWhiteSpace(value) &&
                                   Normalize(value).Contains(normalizedCandidate, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string value)
    {
        string normalized = value.Trim().ToLowerInvariant();
        while (normalized.Length > 2 && char.IsDigit(normalized[^1]))
            normalized = normalized[..^1];

        if (normalized.EndsWith("_", StringComparison.Ordinal))
            normalized = normalized.TrimEnd('_');

        return normalized;
    }

    private static bool IsRaidRelevant(ThanosDependencyItem dependency, string upkPath)
    {
        if (IsRequiredRaidRootDependency(dependency))
            return true;

        string haystack = string.Join(" ", new[]
        {
            dependency.Name,
            dependency.ObjectPath,
            dependency.PackageName,
            dependency.ClassName,
            dependency.OuterName,
            dependency.ReferenceKind,
            upkPath
        }.Where(item => !string.IsNullOrWhiteSpace(item)));

        if (haystack.Contains("thanos", StringComparison.OrdinalIgnoreCase) ||
            haystack.Contains("raid", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (dependency.ObjectPath.StartsWith("theworld.", StringComparison.OrdinalIgnoreCase))
            return true;

        if (dependency.ObjectPath.Contains(".persistentlevel.", StringComparison.OrdinalIgnoreCase))
            return true;

        if (dependency.ReferenceKind.Equals("OuterReference", StringComparison.OrdinalIgnoreCase) ||
            dependency.ReferenceKind.Equals("ImportTable", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return dependency.ReferenceKind is "ClassReference" or "SuperReference" or "ArchetypeReference" or
               "ObjectProperty" or "ClassProperty" or "ComponentProperty" or "InterfaceProperty";
    }

    private static string GetRaidReason(ThanosDependencyItem dependency, string upkPath)
    {
        if (IsRequiredRaidRootDependency(dependency))
            return "required-raid-root";

        if (dependency.ObjectPath.StartsWith("theworld.", StringComparison.OrdinalIgnoreCase))
            return "theworld";

        if (dependency.ObjectPath.Contains(".persistentlevel.", StringComparison.OrdinalIgnoreCase))
            return "persistentlevel";

        if (dependency.ReferenceKind.Equals("ArchetypeReference", StringComparison.OrdinalIgnoreCase))
            return "archetype";

        if (dependency.ReferenceKind.Equals("ClassReference", StringComparison.OrdinalIgnoreCase))
            return "class";

        if (dependency.ReferenceKind.Equals("SuperReference", StringComparison.OrdinalIgnoreCase))
            return "super";

        if (dependency.ReferenceKind.Equals("ObjectProperty", StringComparison.OrdinalIgnoreCase) ||
            dependency.ReferenceKind.Equals("ClassProperty", StringComparison.OrdinalIgnoreCase) ||
            dependency.ReferenceKind.Equals("ComponentProperty", StringComparison.OrdinalIgnoreCase) ||
            dependency.ReferenceKind.Equals("InterfaceProperty", StringComparison.OrdinalIgnoreCase))
        {
            return "property";
        }

        if (upkPath.Contains("thanos", StringComparison.OrdinalIgnoreCase) ||
            upkPath.Contains("raid", StringComparison.OrdinalIgnoreCase))
        {
            return "raid-upk";
        }

        return "raid-relevant";
    }

    private sealed class PrototypeMatch
    {
        public int Score { get; set; }

        public string Reason { get; set; } = string.Empty;
    }
}

