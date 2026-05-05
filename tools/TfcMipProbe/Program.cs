using System.Buffers.Binary;
using UpkManager.Constants;
using UpkManager.Models.UpkFile.Engine.Texture;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Repository;

if (args.Length < 1)
{
    Console.WriteLine("Usage: TfcMipProbe <upkPathOrDirectory>");
    return 2;
}

string inputPath = Path.GetFullPath(args[0]);
if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
{
    Console.WriteLine($"Path not found: {inputPath}");
    return 2;
}

List<string> upkPaths = File.Exists(inputPath)
    ? [inputPath]
    : Directory.EnumerateFiles(inputPath, "*.upk", SearchOption.TopDirectoryOnly)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToList();

if (upkPaths.Count == 0)
{
    Console.WriteLine("No UPK files found.");
    return 0;
}

int totalFiles = 0;
int totalTextureExports = 0;
int totalBadMips = 0;
int totalTruncated = 0;
int totalExportParseErrors = 0;
int totalFileErrors = 0;

foreach (string upkPath in upkPaths)
{
    totalFiles++;
    int textureExports = 0;
    int badMips = 0;
    int truncated = 0;
    int exportParseErrors = 0;

    Console.WriteLine($"UPK={upkPath}");

    try
    {
        UpkFileRepository repo = new();
        var header = await repo.LoadUpkFile(upkPath).ConfigureAwait(false);
        await header.ReadHeaderAsync(null).ConfigureAwait(false);

        foreach (var export in header.ExportTable)
        {
            string className = export.ClassReferenceNameIndex?.Name ?? string.Empty;
            if (!className.Contains("Texture2D", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                await export.ParseUnrealObject(false, false).ConfigureAwait(false);
                if (export.UnrealObject is not IUnrealObject { UObject: UTexture2D texture })
                    continue;

                textureExports++;
                byte[] bytes = export.UnrealObjectReader.GetBytes();
                int cursor = texture.MipArrayOffset + 4;
                for (int mipIndex = 0; mipIndex < texture.Mips.Count; mipIndex++)
                {
                    if (cursor + 16 > bytes.Length)
                    {
                        truncated++;
                        break;
                    }

                    uint flags = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(cursor, 4));
                    int uncompressedSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(cursor + 4, 4));
                    int compressedSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(cursor + 8, 4));
                    int dataOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(cursor + 12, 4));

                    bool hasUnusedOrSeparate = (flags & (uint)(BulkDataCompressionTypes.Unused | BulkDataCompressionTypes.StoreInSeparatefile)) != 0;
                    int payloadSize = hasUnusedOrSeparate ? 0 : Math.Max(0, compressedSize);

                    bool suspicious = dataOffset < 0 && compressedSize > 0 && !hasUnusedOrSeparate;
                    bool exactCrashShape = dataOffset == -1 && compressedSize == 1048576;
                    if (suspicious || exactCrashShape)
                    {
                        badMips++;
                        Console.WriteLine(
                            $"BAD Export={export.GetPathName()} Tfc={(texture.TextureFileCacheName?.Name ?? "<null>")} Mip={mipIndex} Flags=0x{flags:X8} Unc={uncompressedSize} Comp={compressedSize} Off={dataOffset}");
                    }

                    cursor += 16 + payloadSize + 8;
                    if (cursor > bytes.Length)
                    {
                        truncated++;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                exportParseErrors++;
                Console.WriteLine($"EXPORT_PARSE_ERROR Export={export.GetPathName()} Class={className} Error={ex.Message}");
            }
        }

        Console.WriteLine($"SUMMARY TextureExports={textureExports} BadMips={badMips} Truncated={truncated} ExportParseErrors={exportParseErrors}");

        totalTextureExports += textureExports;
        totalBadMips += badMips;
        totalTruncated += truncated;
        totalExportParseErrors += exportParseErrors;
    }
    catch (Exception ex)
    {
        totalFileErrors++;
        Console.WriteLine($"FILE_ERROR {upkPath} :: {ex.Message}");
    }
}

Console.WriteLine(
    $"TOTAL Files={totalFiles} TextureExports={totalTextureExports} BadMips={totalBadMips} Truncated={totalTruncated} ExportParseErrors={totalExportParseErrors} FileErrors={totalFileErrors}");
return 0;
