using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using OmegaAssetStudio.ThanosMigration.Models;
using OmegaAssetStudio.TfcManifest;
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

        if (entries.Count > 0)
            return entries;

        // Fallback for legacy manifests that do not load through TextureManifest parser.
        try
        {
            TfcManifestReader legacyReader = new();
            TfcManifestDocument legacyDocument = legacyReader.Read(fullPath);
            AppendLegacyEntries(entries, legacyDocument.Entries);
        }
        catch
        {
            // Legacy v0 reader path (older 1.34/1.48 manifests with global/current TFC name state).
            AppendLegacyEntries(entries, ParseLegacyManifestV0(fullPath));
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
        ValidateEntryRanges(entries);

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
                    Offset = (uint)entry.Offset,
                    Size = (uint)entry.Size
                })
                .ToList()
        };

        return textureEntry;
    }

    private static void ValidateEntryRanges(IEnumerable<ThanosTfcEntry> entries)
    {
        foreach (ThanosTfcEntry entry in entries)
        {
            if (entry.Offset < 0)
                throw new InvalidOperationException($"Manifest write aborted: negative offset for {entry.TextureName} chunk {entry.ChunkIndex}.");

            if (entry.Size <= 0)
                throw new InvalidOperationException($"Manifest write aborted: non-positive size for {entry.TextureName} chunk {entry.ChunkIndex}.");

            if (entry.Offset > uint.MaxValue)
                throw new InvalidOperationException(
                    $"Manifest write aborted: offset overflow (>4GB) for {entry.TextureName} chunk {entry.ChunkIndex}, offset={entry.Offset}.");

            if (entry.Size > uint.MaxValue)
                throw new InvalidOperationException(
                    $"Manifest write aborted: size overflow (>4GB) for {entry.TextureName} chunk {entry.ChunkIndex}, size={entry.Size}.");
        }
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

    private static void AppendLegacyEntries(List<ThanosTfcEntry> sink, IEnumerable<TfcManifestEntry> source)
    {
        foreach (TfcManifestEntry legacyEntry in source)
        {
            IReadOnlyList<TfcManifestChunk> chunks = legacyEntry.Chunks.Count > 0
                ? legacyEntry.Chunks
                : [new TfcManifestChunk
                {
                    ChunkIndex = legacyEntry.ChunkIndex,
                    Offset = legacyEntry.Offset,
                    Size = legacyEntry.Size
                }];

            foreach (TfcManifestChunk chunk in chunks)
            {
                sink.Add(new ThanosTfcEntry
                {
                    PackageName = string.IsNullOrWhiteSpace(legacyEntry.PackageName) ? legacyEntry.TextureName : legacyEntry.PackageName,
                    TextureName = legacyEntry.TextureName,
                    TextureGuid = legacyEntry.TextureGuid,
                    TfcFileName = legacyEntry.TfcFileName ?? string.Empty,
                    ChunkIndex = chunk.ChunkIndex,
                    Offset = chunk.Offset,
                    Size = chunk.Size
                });
            }
        }
    }

    private static List<TfcManifestEntry> ParseLegacyManifestV0(string path)
    {
        List<TfcManifestEntry> entries = [];
        using BinaryReader reader = new(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read));
        uint entryCount = reader.ReadUInt32();
        string currentTfc = ReadUtf8String(reader);
        if (string.IsNullOrWhiteSpace(currentTfc))
            currentTfc = "Textures";

        for (uint i = 0; i < entryCount; i++)
        {
            if (reader.BaseStream.Position >= reader.BaseStream.Length)
                break;

            string token = ReadUtf8String(reader);
            if (LooksLikeTfcToken(token))
            {
                currentTfc = token;
                if (reader.BaseStream.Position >= reader.BaseStream.Length)
                    break;

                token = ReadUtf8String(reader);
            }

            if (reader.BaseStream.Position + 16 + 4 > reader.BaseStream.Length)
                break;

            Guid textureGuid = new(reader.ReadBytes(16));
            uint mipCount = reader.ReadUInt32();

            TfcManifestEntry entry = new()
            {
                TextureGuid = textureGuid,
                TfcFileName = currentTfc,
                TextureName = ExtractTextureName(token),
                PackageName = ExtractPackageName(token)
            };

            for (uint mip = 0; mip < mipCount; mip++)
            {
                if (reader.BaseStream.Position + 12 > reader.BaseStream.Length)
                    break;

                uint chunkIndex = reader.ReadUInt32();
                uint offset = reader.ReadUInt32();
                uint size = reader.ReadUInt32();
                entry.Chunks.Add(new TfcManifestChunk
                {
                    ChunkIndex = unchecked((int)chunkIndex),
                    Offset = offset,
                    Size = size
                });
            }

            entry.Normalize();
            entries.Add(entry);
        }

        return entries;
    }

    private static bool LooksLikeTfcToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.EndsWith(".tfc", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("textures", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("chartextures", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("texture", StringComparison.OrdinalIgnoreCase) && !value.Contains('.');
    }

    private static string ExtractPackageName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        int lastDot = value.LastIndexOf('.');
        if (lastDot <= 0)
            return string.Empty;

        return value[..lastDot];
    }

    private static string ExtractTextureName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        int lastDot = value.LastIndexOf('.');
        if (lastDot < 0 || lastDot + 1 >= value.Length)
            return value;

        return value[(lastDot + 1)..];
    }

    private static string ReadUtf8String(BinaryReader reader)
    {
        if (reader.BaseStream.Position + 4 > reader.BaseStream.Length)
            return string.Empty;

        uint length = reader.ReadUInt32();
        if (length == 0 || length > int.MaxValue || reader.BaseStream.Position + length > reader.BaseStream.Length)
            return string.Empty;

        byte[] bytes = reader.ReadBytes((int)length);
        int nullIndex = Array.IndexOf(bytes, (byte)0);
        if (nullIndex >= 0)
            bytes = bytes[..nullIndex];

        return Encoding.UTF8.GetString(bytes);
    }
}

