using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration;

public sealed class UpkBackportService
{
    private static readonly Regex[] PackagePatterns =
    [
        new(@"Unable to find package file for package name\s+(?<name>[^\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"Failed to find .*?\[(?<name>[^\]]+)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"Unable to resolve package\s+(?<name>[A-Za-z0-9_./\\-]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<UpkBackportReport> FindMissingPackagesAsync(
        string logPath,
        string sourceRoot,
        string? serverEmuRoot,
        string outputRoot,
        Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(logPath))
            throw new ArgumentException("A log file is required.", nameof(logPath));

        if (!File.Exists(logPath))
            throw new FileNotFoundException("Log file not found.", logPath);

        if (string.IsNullOrWhiteSpace(sourceRoot))
            throw new ArgumentException("A 1.48 source root is required.", nameof(sourceRoot));

        string fullSourceRoot = Path.GetFullPath(sourceRoot);
        string? fullServerRoot = string.IsNullOrWhiteSpace(serverEmuRoot) ? null : Path.GetFullPath(serverEmuRoot);
        string fullOutputRoot = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(fullOutputRoot);

        UpkBackportReport report = new()
        {
            LogPath = Path.GetFullPath(logPath),
            SourceRoot = fullSourceRoot,
            ServerEmuRoot = fullServerRoot,
            OutputRoot = fullOutputRoot
        };

        HashSet<string> detectedPackages = DetectPackages(File.ReadAllLines(logPath));
        foreach (string package in detectedPackages.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
            report.DetectedPackages.Add(package);

        log?.Invoke($"Detected {report.DetectedCount:N0} missing package name(s).");

        foreach (string packageName in report.DetectedPackages)
        {
            string normalizedPackage = NormalizePackageName(packageName);
            if (string.IsNullOrWhiteSpace(normalizedPackage))
                continue;

            report.ExpandedPackages.Add(normalizedPackage);
            UpkBackportPackageStatusRow row = new()
            {
                PackageName = normalizedPackage,
                Status = "Detected"
            };
            report.PackageStatuses.Add(row);
            string? sourceFile = FindPackageFile(normalizedPackage, fullSourceRoot, fullServerRoot);
            if (sourceFile is null)
            {
                report.MissingPackages.Add(normalizedPackage);
                row.Status = "Still missing";
                log?.Invoke($"Missing source package: {normalizedPackage}");
                continue;
            }

            row.SourcePath = sourceFile;
            row.Status = "Found in 1.48";
            string destinationPath = Path.Combine(fullOutputRoot, Path.GetFileName(sourceFile));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourceFile, destinationPath, overwrite: true);
            report.BackportedPackages.Add(destinationPath);
            row.OutputPath = destinationPath;
            row.Status = "Backported";
            log?.Invoke($"Backported {Path.GetFileName(sourceFile)}");
        }

        report.Notes.Add($"Detected {report.DetectedCount:N0} package name(s) from {Path.GetFileName(logPath)}.");
        report.Notes.Add($"Backported {report.BackportedCount:N0} package file(s).");
        report.Notes.Add($"Missing source packages: {report.MissingPackages.Count:N0}.");

        await ExportAsync(report, fullOutputRoot, logPath).ConfigureAwait(false);
        return report;
    }

    public async Task<string> ExportAsync(UpkBackportReport report, string outputRoot, string? logPath = null)
    {
        string fullOutputRoot = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(fullOutputRoot);

        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string jsonPath = Path.Combine(fullOutputRoot, $"BackportReport_{stamp}.json");
        string markdownPath = Path.Combine(fullOutputRoot, $"BackportReport_{stamp}.md");

        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, JsonOptions)).ConfigureAwait(false);
        await File.WriteAllTextAsync(markdownPath, BuildMarkdown(report)).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(logPath))
        {
            string logFile = Path.Combine(fullOutputRoot, $"BackportReport_{stamp}.log");
            await File.WriteAllTextAsync(logFile, string.Join(Environment.NewLine, report.Notes)).ConfigureAwait(false);
        }

        return fullOutputRoot;
    }

    private static HashSet<string> DetectPackages(IEnumerable<string> lines)
    {
        HashSet<string> packages = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            foreach (Regex pattern in PackagePatterns)
            {
                Match match = pattern.Match(line);
                if (!match.Success)
                    continue;

                string name = NormalizePackageName(match.Groups["name"].Value);
                if (!string.IsNullOrWhiteSpace(name))
                    packages.Add(name);
            }
        }

        return packages;
    }

    private static string NormalizePackageName(string value)
    {
        string result = value.Trim().Trim('"', '\'', '[', ']', '(', ')', '{', '}');
        if (result.EndsWith(".upk", StringComparison.OrdinalIgnoreCase))
            result = Path.GetFileNameWithoutExtension(result);

        return result.Trim();
    }

    private static string? FindPackageFile(string packageName, string sourceRoot, string? serverEmuRoot)
    {
        string[] roots = serverEmuRoot is null
            ? [sourceRoot]
            : [sourceRoot, serverEmuRoot];

        foreach (string root in roots.Where(Directory.Exists))
        {
            string? file = Directory
                .EnumerateFiles(root, "*.upk", SearchOption.AllDirectories)
                .FirstOrDefault(path => string.Equals(Path.GetFileNameWithoutExtension(path), packageName, StringComparison.OrdinalIgnoreCase));
            if (file is not null)
                return file;
        }

        return null;
    }

    private static string BuildMarkdown(UpkBackportReport report)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Backport Report");
        builder.AppendLine();
        builder.AppendLine($"- Log: {report.LogPath}");
        builder.AppendLine($"- Source root: {report.SourceRoot}");
        builder.AppendLine($"- ServerEmu root: {report.ServerEmuRoot ?? "(none)"}");
        builder.AppendLine($"- Output root: {report.OutputRoot}");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine(report.SummaryText);
        builder.AppendLine();
        builder.AppendLine("## Backported Packages");
        foreach (string item in report.BackportedPackages)
            builder.AppendLine($"- {item}");
        builder.AppendLine();
        builder.AppendLine("## Missing Packages");
        foreach (string item in report.MissingPackages)
            builder.AppendLine($"- {item}");
        builder.AppendLine();
        builder.AppendLine("## Package Statuses");
        foreach (UpkBackportPackageStatusRow row in report.PackageStatuses)
            builder.AppendLine($"- {row.PackageName} | {row.Status} | {row.SourcePath} | {row.OutputPath} | {row.DeployedPath}");
        return builder.ToString();
    }
}