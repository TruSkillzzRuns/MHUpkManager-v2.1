using System.Text.RegularExpressions;
using OmegaAssetStudio.ThanosMigration.Models;

namespace OmegaAssetStudio.ThanosMigration.Services;

public sealed class ThanosTextureCacheInjectorService
{
    private const string ManifestFileName = "TextureFileCacheManifest.bin";
    private static readonly string[] IncludeKeywords = ["knowhere", "thanos"];
    private static readonly string[] ExcludeKeywords = ["gauntlet"];
    private static readonly Regex BackupTimestampRegex = new(@"\.(\d{8}_\d{6})\.bak$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly TfcManifestService manifestService = new();

    public ThanosTextureCacheInjectionResult InjectMissingEntries(
        string source148Directory,
        string target152Directory,
        Action<double, string>? progress = null,
        Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source148Directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(target152Directory);

        string sourceRoot = Path.GetFullPath(source148Directory);
        string targetRoot = Path.GetFullPath(target152Directory);
        string sourceManifestPath = Path.Combine(sourceRoot, ManifestFileName);
        string targetManifestPath = Path.Combine(targetRoot, ManifestFileName);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string transactionDirectory = Path.Combine(targetRoot, $"_oas_tfc_txn_{timestamp}");

        if (!File.Exists(sourceManifestPath))
            throw new FileNotFoundException("Source 1.48 TextureFileCacheManifest.bin was not found.", sourceManifestPath);

        if (!File.Exists(targetManifestPath))
            throw new FileNotFoundException("Target 1.52 TextureFileCacheManifest.bin was not found.", targetManifestPath);

        ThanosTextureCacheInjectionResult result = new()
        {
            TransactionDirectory = transactionDirectory
        };

        Dictionary<string, string> createdBackups = new(StringComparer.OrdinalIgnoreCase);
        bool committed = false;

        try
        {
            progress?.Invoke(5, "Loading source and target manifests...");
            List<ThanosTfcEntry> sourceManifest = manifestService.LoadManifest(sourceManifestPath);
            List<ThanosTfcEntry> targetManifest = manifestService.LoadManifest(targetManifestPath);
            result.TargetEntriesBefore = targetManifest.Count;

            List<ThanosTfcEntry> sourceCandidates = sourceManifest
                .Where(IsThanosKnowhereEntry)
                .ToList();
            result.SourceCandidateEntries = sourceCandidates.Count;

            if (sourceCandidates.Count == 0)
            {
                result.Warnings.Add("No source manifest entries matched Knowhere/Thanos keywords.");
                progress?.Invoke(100, "No matching Knowhere/Thanos entries found.");
                return result;
            }

            result.PreflightSourceChunks = sourceCandidates.Count;
            PreflightSourceCandidates(sourceRoot, sourceCandidates);
            result.PreflightPassed = true;
            progress?.Invoke(15, $"Preflight passed ({result.PreflightSourceChunks:N0} source chunks).");

            EnsureBackup(targetManifestPath, timestamp, createdBackups, result, log);

            Directory.CreateDirectory(transactionDirectory);
            Dictionary<string, string> tempTargetTfcPathsByName = CreateTransactionalTargetCopies(
                sourceCandidates,
                targetRoot,
                transactionDirectory,
                timestamp,
                createdBackups,
                result,
                log);

            result.PreflightTargetChunks = CountRelevantTargetChunks(targetManifest, sourceCandidates);

            Dictionary<string, List<ThanosTfcEntry>> targetByKey = targetManifest
                .GroupBy(BuildTextureKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.OrderBy(static entry => entry.ChunkIndex).ToList(), StringComparer.OrdinalIgnoreCase);

            int totalSourceChunks = Math.Max(1, sourceCandidates.Count);
            int processedSourceChunks = 0;
            void OnSourceChunkVisited(string phase)
            {
                processedSourceChunks++;
                double span = 70.0;
                double pct = 20.0 + (processedSourceChunks * span / totalSourceChunks);
                progress?.Invoke(Math.Min(90, pct), $"{phase} ({processedSourceChunks:N0}/{totalSourceChunks:N0} chunks)");
            }

            List<IGrouping<string, ThanosTfcEntry>> sourceTextureGroups = sourceCandidates
                .GroupBy(BuildTextureKey, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int processedTextures = 0;
            foreach (IGrouping<string, ThanosTfcEntry> sourceGroup in sourceTextureGroups)
            {
                string key = sourceGroup.Key;
                List<ThanosTfcEntry> sourceChunks = sourceGroup.OrderBy(static entry => entry.ChunkIndex).ToList();

                if (!targetByKey.TryGetValue(key, out List<ThanosTfcEntry>? targetChunks))
                {
                    List<ThanosTfcEntry> injected = InjectEntireTextureEntry(
                        sourceRoot,
                        tempTargetTfcPathsByName,
                        sourceChunks,
                        result,
                        OnSourceChunkVisited,
                        log);

                    targetManifest.AddRange(injected);
                    targetByKey[key] = injected;
                    result.AddedTextureEntries++;
                    result.SourceMatchedEntries++;
                }
                else
                {
                    int patchedChunkCount = PatchMissingOrInvalidChunks(
                        sourceRoot,
                        tempTargetTfcPathsByName,
                        sourceChunks,
                        targetChunks,
                        result,
                        OnSourceChunkVisited,
                        log);

                    if (patchedChunkCount > 0)
                    {
                        result.PatchedTextureEntries++;
                        result.SourceMatchedEntries++;
                    }
                }

                processedTextures++;
                log?.Invoke($"Processed texture {processedTextures} of {sourceTextureGroups.Count}: {sourceChunks[0].TextureName}");
            }

            string tempManifestPath = Path.Combine(transactionDirectory, ManifestFileName);
            manifestService.SaveManifest(tempManifestPath, targetManifest);
            log?.Invoke($"Wrote transactional manifest: {tempManifestPath}");

            progress?.Invoke(92, "Running post-inject verification...");
            RunPostInjectVerification(sourceCandidates, targetManifest, tempTargetTfcPathsByName, result);
            if (result.VerificationFailures > 0)
            {
                string failure = result.VerificationErrors.FirstOrDefault() ?? "Unknown verification error.";
                throw new InvalidOperationException($"Post-inject verification failed ({result.VerificationFailures}). {failure}");
            }

            SwapTransactionIntoPlace(
                tempTargetTfcPathsByName,
                tempManifestPath,
                targetManifestPath,
                result,
                log);
            committed = true;
            result.UsedTransactionalSwap = true;
            result.TargetEntriesAfter = targetManifest.Count;

            progress?.Invoke(100, "Knowhere profile complete (preflight, inject, verify).");
            return result;
        }
        finally
        {
            if (committed && Directory.Exists(transactionDirectory))
            {
                try
                {
                    Directory.Delete(transactionDirectory, recursive: true);
                }
                catch
                {
                }
            }
        }
    }

    public ThanosTextureCacheRollbackResult RollbackLatestBackup(string target152Directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target152Directory);

        string targetRoot = Path.GetFullPath(target152Directory);
        string[] backupFiles = Directory.Exists(targetRoot)
            ? Directory.GetFiles(targetRoot, "*.bak", SearchOption.TopDirectoryOnly)
            : [];

        List<(string BackupPath, string Timestamp, string OriginalPath)> parsed = [];
        foreach (string backupPath in backupFiles)
        {
            Match match = BackupTimestampRegex.Match(backupPath);
            if (!match.Success)
                continue;

            string timestamp = match.Groups[1].Value;
            string originalPath = backupPath[..match.Index];
            parsed.Add((backupPath, timestamp, originalPath));
        }

        ThanosTextureCacheRollbackResult result = new();
        if (parsed.Count == 0)
            return result;

        string latestTimestamp = parsed
            .Select(static item => item.Timestamp)
            .OrderByDescending(static item => item, StringComparer.Ordinal)
            .First();

        result.Timestamp = latestTimestamp;

        foreach ((string BackupPath, string Timestamp, string OriginalPath) entry in parsed.Where(item => item.Timestamp == latestTimestamp))
        {
            if (!File.Exists(entry.BackupPath))
            {
                result.MissingBackupPaths.Add(entry.BackupPath);
                continue;
            }

            File.Copy(entry.BackupPath, entry.OriginalPath, overwrite: true);
            result.RestoredPaths.Add(entry.OriginalPath);
        }

        result.Restored = result.RestoredPaths.Count > 0;
        return result;
    }

    private static void PreflightSourceCandidates(string sourceRoot, List<ThanosTfcEntry> sourceCandidates)
    {
        foreach (ThanosTfcEntry sourceChunk in sourceCandidates)
        {
            string sourceTfcPath = ResolveTfcPath(sourceRoot, sourceChunk.TfcFileName);
            if (!File.Exists(sourceTfcPath))
                throw new FileNotFoundException("Preflight failed: source TFC file missing.", sourceTfcPath);

            if (sourceChunk.Offset < 0 || sourceChunk.Size <= 0)
                throw new InvalidOperationException($"Preflight failed: invalid source chunk {sourceChunk.Offset}:{sourceChunk.Size} in {sourceChunk.TextureName}.");

            long sourceLength = new FileInfo(sourceTfcPath).Length;
            long end = sourceChunk.Offset + sourceChunk.Size;
            if (end > sourceLength)
                throw new InvalidOperationException($"Preflight failed: chunk exceeds source file ({end}>{sourceLength}) for {sourceChunk.TextureName}.");
        }
    }

    private static int CountRelevantTargetChunks(List<ThanosTfcEntry> targetManifest, List<ThanosTfcEntry> sourceCandidates)
    {
        HashSet<string> keys = sourceCandidates
            .Select(BuildTextureKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return targetManifest.Count(entry => keys.Contains(BuildTextureKey(entry)));
    }

    private static Dictionary<string, string> CreateTransactionalTargetCopies(
        List<ThanosTfcEntry> sourceCandidates,
        string targetRoot,
        string transactionDirectory,
        string timestamp,
        Dictionary<string, string> createdBackups,
        ThanosTextureCacheInjectionResult result,
        Action<string>? log)
    {
        Dictionary<string, string> tempPaths = new(StringComparer.OrdinalIgnoreCase);
        foreach (string normalizedTfcName in sourceCandidates
                     .Select(static entry => NormalizeTfcFileName(entry.TfcFileName))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string targetTfcPath = Path.Combine(targetRoot, normalizedTfcName);
            if (!File.Exists(targetTfcPath))
                throw new FileNotFoundException("Preflight failed: target TFC file missing.", targetTfcPath);

            EnsureBackup(targetTfcPath, timestamp, createdBackups, result, log);

            string tempPath = Path.Combine(transactionDirectory, normalizedTfcName);
            File.Copy(targetTfcPath, tempPath, overwrite: true);
            tempPaths[normalizedTfcName] = tempPath;
            if (!result.TouchedTargetTfcFiles.Contains(targetTfcPath, StringComparer.OrdinalIgnoreCase))
                result.TouchedTargetTfcFiles.Add(targetTfcPath);
        }

        return tempPaths;
    }

    private List<ThanosTfcEntry> InjectEntireTextureEntry(
        string sourceRoot,
        IReadOnlyDictionary<string, string> tempTargetTfcPathsByName,
        List<ThanosTfcEntry> sourceChunks,
        ThanosTextureCacheInjectionResult result,
        Action<string> onSourceChunkVisited,
        Action<string>? log)
    {
        List<ThanosTfcEntry> injected = [];
        foreach (ThanosTfcEntry sourceChunk in sourceChunks.OrderBy(static entry => entry.ChunkIndex))
        {
            onSourceChunkVisited("Injecting");
            ThanosTfcEntry injectedChunk = AppendChunkFromSource(sourceRoot, tempTargetTfcPathsByName, sourceChunk, result, log);
            injected.Add(injectedChunk);
            result.AddedChunks++;
        }

        return injected;
    }

    private int PatchMissingOrInvalidChunks(
        string sourceRoot,
        IReadOnlyDictionary<string, string> tempTargetTfcPathsByName,
        List<ThanosTfcEntry> sourceChunks,
        List<ThanosTfcEntry> targetChunks,
        ThanosTextureCacheInjectionResult result,
        Action<string> onSourceChunkVisited,
        Action<string>? log)
    {
        Dictionary<int, ThanosTfcEntry> targetByChunkIndex = targetChunks
            .GroupBy(static chunk => chunk.ChunkIndex)
            .ToDictionary(static group => group.Key, static group => group.First());

        int patchedCount = 0;
        foreach (ThanosTfcEntry sourceChunk in sourceChunks.OrderBy(static entry => entry.ChunkIndex))
        {
            onSourceChunkVisited("Scanning");
            if (targetByChunkIndex.TryGetValue(sourceChunk.ChunkIndex, out ThanosTfcEntry? existing))
            {
                if (!IsInvalidTargetChunk(existing, tempTargetTfcPathsByName))
                    continue;

                ThanosTfcEntry replaced = AppendChunkFromSource(sourceRoot, tempTargetTfcPathsByName, sourceChunk, result, log);
                existing.Offset = replaced.Offset;
                existing.Size = replaced.Size;
                existing.TfcFileName = replaced.TfcFileName;
                existing.TextureGuid = replaced.TextureGuid;
                existing.PackageName = replaced.PackageName;
                existing.TextureName = replaced.TextureName;
                patchedCount++;
                result.AddedChunks++;
                result.ReplacedInvalidChunks++;
                continue;
            }

            ThanosTfcEntry injectedChunk = AppendChunkFromSource(sourceRoot, tempTargetTfcPathsByName, sourceChunk, result, log);
            targetChunks.Add(injectedChunk);
            targetByChunkIndex[sourceChunk.ChunkIndex] = injectedChunk;
            patchedCount++;
            result.AddedChunks++;
        }

        return patchedCount;
    }

    private static bool IsInvalidTargetChunk(ThanosTfcEntry chunk, IReadOnlyDictionary<string, string> tempTargetTfcPathsByName)
    {
        if (chunk.Size <= 0)
            return true;

        if (chunk.Offset < 0 || chunk.Offset == uint.MaxValue)
            return true;

        string normalizedName = NormalizeTfcFileName(chunk.TfcFileName);
        if (!tempTargetTfcPathsByName.TryGetValue(normalizedName, out string? targetTfcPath))
            return true;

        if (!File.Exists(targetTfcPath))
            return true;

        long end = chunk.Offset + chunk.Size;
        return end > new FileInfo(targetTfcPath).Length;
    }

    private ThanosTfcEntry AppendChunkFromSource(
        string sourceRoot,
        IReadOnlyDictionary<string, string> tempTargetTfcPathsByName,
        ThanosTfcEntry sourceChunk,
        ThanosTextureCacheInjectionResult result,
        Action<string>? log)
    {
        string sourceTfcPath = ResolveTfcPath(sourceRoot, sourceChunk.TfcFileName);
        string normalizedName = NormalizeTfcFileName(sourceChunk.TfcFileName);
        if (!tempTargetTfcPathsByName.TryGetValue(normalizedName, out string? targetTempTfcPath))
            throw new InvalidOperationException($"Missing transaction target for {sourceChunk.TfcFileName}.");

        if (!File.Exists(sourceTfcPath))
            throw new FileNotFoundException("Source TFC file was not found.", sourceTfcPath);

        if (!File.Exists(targetTempTfcPath))
            throw new FileNotFoundException("Transaction target TFC file was not found.", targetTempTfcPath);

        long offset;
        using (FileStream sourceStream = new(sourceTfcPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (FileStream targetStream = new(targetTempTfcPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            if (sourceChunk.Offset < 0 || sourceChunk.Size <= 0)
                throw new InvalidOperationException($"Invalid source chunk range {sourceChunk.Offset}:{sourceChunk.Size} in {sourceTfcPath}.");

            long end = sourceChunk.Offset + sourceChunk.Size;
            if (end > sourceStream.Length)
                throw new InvalidOperationException($"Source chunk range {sourceChunk.Offset}:{sourceChunk.Size} exceeds file length {sourceStream.Length} for {sourceTfcPath}.");

            sourceStream.Seek(sourceChunk.Offset, SeekOrigin.Begin);
            targetStream.Seek(0, SeekOrigin.End);
            offset = targetStream.Position;
            long remaining = sourceChunk.Size;
            byte[] buffer = new byte[1024 * 1024];
            while (remaining > 0)
            {
                int readSize = (int)Math.Min(buffer.Length, remaining);
                int read = sourceStream.Read(buffer, 0, readSize);
                if (read <= 0)
                    throw new IOException($"Unexpected end of stream while reading {sourceTfcPath}.");

                targetStream.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        log?.Invoke($"Injected {sourceChunk.Size:N0} bytes into {normalizedName} at offset {offset:N0}.");
        return new ThanosTfcEntry
        {
            PackageName = sourceChunk.PackageName,
            TextureName = sourceChunk.TextureName,
            TextureGuid = sourceChunk.TextureGuid,
            TfcFileName = sourceChunk.TfcFileName,
            ChunkIndex = sourceChunk.ChunkIndex,
            Offset = offset,
            Size = sourceChunk.Size
        };
    }

    private static void RunPostInjectVerification(
        List<ThanosTfcEntry> sourceCandidates,
        List<ThanosTfcEntry> targetManifest,
        IReadOnlyDictionary<string, string> tempTargetTfcPathsByName,
        ThanosTextureCacheInjectionResult result)
    {
        Dictionary<string, Dictionary<int, ThanosTfcEntry>> targetLookup = targetManifest
            .GroupBy(BuildTextureKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .GroupBy(static entry => entry.ChunkIndex)
                    .ToDictionary(static chunkGroup => chunkGroup.Key, static chunkGroup => chunkGroup.First()),
                StringComparer.OrdinalIgnoreCase);

        foreach (ThanosTfcEntry sourceChunk in sourceCandidates)
        {
            string key = BuildTextureKey(sourceChunk);
            if (!targetLookup.TryGetValue(key, out Dictionary<int, ThanosTfcEntry>? chunkMap) ||
                !chunkMap.TryGetValue(sourceChunk.ChunkIndex, out ThanosTfcEntry? targetChunk))
            {
                result.VerificationFailures++;
                result.VerificationErrors.Add($"Missing chunk after inject: {sourceChunk.TextureName} chunk {sourceChunk.ChunkIndex}.");
                continue;
            }

            if (IsInvalidTargetChunk(targetChunk, tempTargetTfcPathsByName))
            {
                result.VerificationFailures++;
                result.VerificationErrors.Add($"Invalid chunk after inject: {sourceChunk.TextureName} chunk {sourceChunk.ChunkIndex}, offset={targetChunk.Offset}, size={targetChunk.Size}.");
                continue;
            }

            result.VerifiedChunks++;
        }
    }

    private static void SwapTransactionIntoPlace(
        IReadOnlyDictionary<string, string> tempTargetTfcPathsByName,
        string tempManifestPath,
        string targetManifestPath,
        ThanosTextureCacheInjectionResult result,
        Action<string>? log)
    {
        foreach (KeyValuePair<string, string> pair in tempTargetTfcPathsByName)
        {
            string normalizedName = pair.Key;
            string tempPath = pair.Value;
            string targetPath = Path.Combine(Path.GetDirectoryName(targetManifestPath) ?? string.Empty, normalizedName);
            SwapFile(tempPath, targetPath);
            result.SwappedTargetPaths.Add(targetPath);
            log?.Invoke($"Committed transactional file: {targetPath}");
        }

        SwapFile(tempManifestPath, targetManifestPath);
        result.SwappedTargetPaths.Add(targetManifestPath);
        log?.Invoke($"Committed transactional manifest: {targetManifestPath}");
    }

    private static void SwapFile(string sourcePath, string destinationPath)
    {
        try
        {
            if (File.Exists(destinationPath))
            {
                File.Replace(sourcePath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Copy(sourcePath, destinationPath, overwrite: true);
                File.Delete(sourcePath);
            }
        }
        catch
        {
            File.Copy(sourcePath, destinationPath, overwrite: true);
            try
            {
                File.Delete(sourcePath);
            }
            catch
            {
            }
        }
    }

    private static bool IsThanosKnowhereEntry(ThanosTfcEntry entry)
    {
        string candidate = $"{entry.PackageName}|{entry.TextureName}|{entry.TfcFileName}".ToLowerInvariant();
        if (ExcludeKeywords.Any(candidate.Contains))
            return false;

        return IncludeKeywords.Any(candidate.Contains);
    }

    private static string BuildTextureKey(ThanosTfcEntry entry)
    {
        if (entry.TextureGuid != Guid.Empty)
            return $"guid:{entry.TextureGuid:N}";

        return $"name:{entry.PackageName}|{entry.TextureName}";
    }

    private static string NormalizeTfcFileName(string tfcFileName)
    {
        if (string.IsNullOrWhiteSpace(tfcFileName))
            throw new InvalidOperationException("Manifest entry is missing TFC file name.");

        return tfcFileName.EndsWith(".tfc", StringComparison.OrdinalIgnoreCase)
            ? tfcFileName
            : $"{tfcFileName}.tfc";
    }

    private static string ResolveTfcPath(string rootDirectory, string tfcFileName)
        => Path.Combine(rootDirectory, NormalizeTfcFileName(tfcFileName));

    private static void EnsureBackup(
        string targetPath,
        string timestamp,
        IDictionary<string, string> createdBackups,
        ThanosTextureCacheInjectionResult result,
        Action<string>? log)
    {
        if (createdBackups.ContainsKey(targetPath))
            return;

        string backupPath = $"{targetPath}.{timestamp}.bak";
        File.Copy(targetPath, backupPath, overwrite: false);
        createdBackups[targetPath] = backupPath;
        result.BackupPaths.Add(backupPath);
        log?.Invoke($"Backup created: {backupPath}");
    }
}
