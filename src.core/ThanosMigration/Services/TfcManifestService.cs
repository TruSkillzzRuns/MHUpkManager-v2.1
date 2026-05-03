using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using OmegaAssetStudio.ThanosMigration.Models;
using OmegaAssetStudio.TextureManager;

namespace OmegaAssetStudio.ThanosMigration.Services;

public sealed class TfcManifestService
{
    public List<ThanosTfcEntry> LoadManifest(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        string fullPath = Path.GetFullPath(manifestPath);
        if (!File.Exists(fullPath))
            return [];

        TextureManifest.Initialize();
        TextureManifest.Instance.LoadManifest(fullPath);

        List<ThanosTfcEntry> entries = [];
        foreach (var pair in TextureManifest.Instance.Entries.OrderBy(static entry => entry.Key.HashIndex))
        {
            foreach (TextureMipMap map in pair.Value.Data.Maps)
            {
                entries.Add(new ThanosTfcEntry
                {
                    PackageName = pair.Key.TextureName,
                    TextureName = pair.Key.TextureName,
                    TextureGuid = pair.Key.TextureGuid,
                    TfcFileName = pair.Value.Data.TextureFileName ?? string.Empty,
                    ChunkIndex = (int)map.Index,
                    Offset = map.Offset,
                    Size = map.Size
                });
            }
        }

        return entries;
    }

    public List<ThanosTfcEntry> MergeEntries(List<ThanosTfcEntry> existing, List<ThanosTfcEntry> newEntries)
    {
        Dictionary<string, ThanosTfcEntry> merged = new(StringComparer.OrdinalIgnoreCase);

        foreach (ThanosTfcEntry entry in existing)
            merged[BuildKey(entry)] = entry;

        foreach (ThanosTfcEntry entry in newEntries)
        {
            string key = BuildKey(entry);
            if (!merged.TryGetValue(key, out ThanosTfcEntry? current))
            {
                merged[key] = entry;
                continue;
            }

            // Preserve an already-valid target entry. Only replace when existing is clearly invalid.
            if (IsInvalidEntry(current) && !IsInvalidEntry(entry))
                merged[key] = entry;
        }

        return merged.Values
            .OrderBy(static entry => entry.PackageName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.TextureName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.TfcFileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.ChunkIndex)
            .ToList();
    }

    private static bool IsInvalidEntry(ThanosTfcEntry entry)
        => entry.Size <= 0 || entry.Offset < 0 || entry.Offset == uint.MaxValue;

    public void SaveManifest(string manifestPath, List<ThanosTfcEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        string fullPath = Path.GetFullPath(manifestPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory);

        TextureManifest.Initialize();
        TextureManifest.Instance.Entries.Clear();

        uint hashIndex = 0;
        foreach (IGrouping<string, ThanosTfcEntry> group in entries
                     .OrderBy(static entry => entry.PackageName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static entry => entry.TextureName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static entry => entry.TfcFileName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static entry => entry.ChunkIndex)
                     .GroupBy(static entry => $"{entry.TextureGuid:N}|{entry.PackageName}|{entry.TextureName}|{entry.TfcFileName}", StringComparer.OrdinalIgnoreCase))
        {
            ThanosTfcEntry first = group.First();
            Guid textureGuid = first.TextureGuid == Guid.Empty
                ? CreateDeterministicGuid(first.PackageName, first.TextureName, first.TfcFileName)
                : first.TextureGuid;

            TextureHead head = new(first.TextureName, textureGuid)
            {
                HashIndex = hashIndex++
            };

            TextureEntry textureEntry = CreateTextureEntry(head, group.ToList());
            TextureManifest.Instance.Entries.Add(head, textureEntry);
        }

        TextureManifest.Instance.SaveManifest(fullPath);
    }

    private static TextureEntry CreateTextureEntry(TextureHead head, List<ThanosTfcEntry> entries)
    {
        TextureEntry textureEntry = new();
        textureEntry.Head = head;
        textureEntry.Data = new TextureMipMaps
        {
            TextureFileName = entries.FirstOrDefault()?.TfcFileName ?? string.Empty,
            Maps = entries
                .OrderBy(static entry => entry.ChunkIndex)
                .Select(entry => new TextureMipMap
                {
                    Index = (uint)Math.Max(0, entry.ChunkIndex),
                    Offset = (uint)Math.Max(0L, entry.Offset),
                    Size = (uint)Math.Max(0L, entry.Size)
                })
                .ToList()
        };

        return textureEntry;
    }

    private static string BuildKey(ThanosTfcEntry entry)
        => $"{entry.TextureGuid:N}|{entry.PackageName}|{entry.TextureName}|{entry.TfcFileName}|{entry.ChunkIndex}";

    private static Guid CreateDeterministicGuid(string packageName, string textureName, string tfcFileName)
    {
        string seed = $"{packageName}|{textureName}|{tfcFileName}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes);
    }
}

