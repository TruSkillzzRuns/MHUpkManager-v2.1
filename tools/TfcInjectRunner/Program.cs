using System.Reflection;
using System.Runtime.Loader;

if (args.Length < 1)
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  inspect <dll> <sourceManifest> <targetManifest>");
    Console.WriteLine("  inject  <dll> <sourceCookedPCConsoleDir> <targetCookedPCConsoleDir>");
    return;
}

string mode = args[0].Trim().ToLowerInvariant();

static Assembly LoadWithLocalDependencyResolver(string dllPath)
{
    string fullDllPath = Path.GetFullPath(dllPath);
    string? baseDir = Path.GetDirectoryName(fullDllPath);
    if (string.IsNullOrWhiteSpace(baseDir))
        throw new InvalidOperationException($"Unable to determine assembly directory for {fullDllPath}.");

    AssemblyLoadContext loadContext = new($"tfc-inject-runner-{Path.GetFileNameWithoutExtension(fullDllPath)}-{Guid.NewGuid():N}", isCollectible: false);
    loadContext.Resolving += static (context, name) =>
    {
        try
        {
            if (context is null)
                return null;

            if (string.IsNullOrWhiteSpace(name.Name))
                return null;

            string? anchor = context.Assemblies.FirstOrDefault()?.Location;
            if (string.IsNullOrWhiteSpace(anchor))
                return null;

            string dir = Path.GetDirectoryName(anchor)!;
            string candidate = Path.Combine(dir, $"{name.Name}.dll");
            if (!File.Exists(candidate))
                return null;

            return context.LoadFromAssemblyPath(candidate);
        }
        catch
        {
            return null;
        }
    };

    return loadContext.LoadFromAssemblyPath(fullDllPath);
}

if (mode == "inspect")
{
    if (args.Length < 4)
    {
        Console.WriteLine("Usage: inspect <dll> <sourceManifest> <targetManifest>");
        return;
    }

    string dllPath = Path.GetFullPath(args[1]);
    string sourceManifest = Path.GetFullPath(args[2]);
    string targetManifest = Path.GetFullPath(args[3]);

    Assembly asm = LoadWithLocalDependencyResolver(dllPath);
    Type svcType = asm.GetType("OmegaAssetStudio.ThanosMigration.Services.TfcManifestService", throwOnError: true)!;
    object svc = Activator.CreateInstance(svcType)!;
    MethodInfo load = svcType.GetMethod("LoadManifest", BindingFlags.Public | BindingFlags.Instance)!;

    var src = ((System.Collections.IEnumerable)load.Invoke(svc, [sourceManifest])!).Cast<object>().ToList();
    var tgt = ((System.Collections.IEnumerable)load.Invoke(svc, [targetManifest])!).Cast<object>().ToList();

    (long off, long size, string pkg, string tex, string tfc, int idx) Read(object e)
    {
        var t = e.GetType();
        return (
            Convert.ToInt64(t.GetProperty("Offset")!.GetValue(e) ?? 0L),
            Convert.ToInt64(t.GetProperty("Size")!.GetValue(e) ?? 0L),
            Convert.ToString(t.GetProperty("PackageName")!.GetValue(e)) ?? "",
            Convert.ToString(t.GetProperty("TextureName")!.GetValue(e)) ?? "",
            Convert.ToString(t.GetProperty("TfcFileName")!.GetValue(e)) ?? "",
            Convert.ToInt32(t.GetProperty("ChunkIndex")!.GetValue(e) ?? 0)
        );
    }

    bool IsInvalid(long off, long size, string tfc, Dictionary<string, long> lengths)
    {
        if (size <= 0) return true;
        if (off < 0 || off == uint.MaxValue) return true;
        string n = tfc.EndsWith(".tfc", StringComparison.OrdinalIgnoreCase) ? tfc : $"{tfc}.tfc";
        if (!lengths.TryGetValue(n, out var len)) return true;
        return off + size > len;
    }

    string root = Path.GetDirectoryName(targetManifest)!;
    var lengths = Directory.EnumerateFiles(root, "*.tfc", SearchOption.TopDirectoryOnly)
        .ToDictionary(p => Path.GetFileName(p), p => new FileInfo(p).Length, StringComparer.OrdinalIgnoreCase);

    int invalidAll = 0, invalidKnowhere = 0, invalidThanos = 0;
    var samples = new List<string>();
    foreach (var e in tgt)
    {
        var r = Read(e);
        if (!IsInvalid(r.off, r.size, r.tfc, lengths)) continue;
        invalidAll++;
        string c = (r.pkg + "|" + r.tex + "|" + r.tfc).ToLowerInvariant();
        if (c.Contains("knowhere")) invalidKnowhere++;
        if (c.Contains("thanos")) invalidThanos++;
        if (samples.Count < 30 && (c.Contains("knowhere") || c.Contains("thanos") || r.off == uint.MaxValue || r.off < 0))
            samples.Add($"{r.pkg}|{r.tex}|{r.tfc}|idx={r.idx}|off={r.off}|size={r.size}");
    }

    Console.WriteLine($"SOURCE_COUNT={src.Count}");
    Console.WriteLine($"TARGET_COUNT={tgt.Count}");
    Console.WriteLine($"INVALID_ALL={invalidAll}");
    Console.WriteLine($"INVALID_KNOWHERE={invalidKnowhere}");
    Console.WriteLine($"INVALID_THANOS={invalidThanos}");
    Console.WriteLine("SAMPLES_START");
    foreach (var s in samples) Console.WriteLine(s);
    Console.WriteLine("SAMPLES_END");
    return;
}

if (mode == "inject")
{
    if (args.Length < 4)
    {
        Console.WriteLine("Usage: inject <dll> <sourceCookedPCConsoleDir> <targetCookedPCConsoleDir>");
        return;
    }

    string dllPath = Path.GetFullPath(args[1]);
    string sourceDir = Path.GetFullPath(args[2]);
    string targetDir = Path.GetFullPath(args[3]);

    Assembly asm = LoadWithLocalDependencyResolver(dllPath);
    Type svcType = asm.GetType("OmegaAssetStudio.ThanosMigration.Services.ThanosTextureCacheInjectorService", throwOnError: true)!;
    object svc = Activator.CreateInstance(svcType)!;
    MethodInfo inject = svcType.GetMethod("InjectMissingEntries", BindingFlags.Public | BindingFlags.Instance)!;

    Action<double, string> progress = (pct, message) => Console.WriteLine($"PROGRESS {pct:0.##}% {message}");
    Action<string> log = message => Console.WriteLine($"LOG {message}");

    object? result = inject.Invoke(svc, [sourceDir, targetDir, progress, log]);
    if (result is null)
    {
        Console.WriteLine("ERROR: injector returned null result.");
        return;
    }

    Type rt = result.GetType();
    void Dump(string name)
    {
        PropertyInfo? p = rt.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        Console.WriteLine($"{name}={p?.GetValue(result)}");
    }

    Dump("SourceCandidateMode");
    Dump("PreflightPassed");
    Dump("PreflightSourceChunks");
    Dump("PreflightTargetChunks");
    Dump("SourceCandidateEntries");
    Dump("DependencySourceUpkCount");
    Dump("DependencyTextureSignalCount");
    Dump("SourceMatchedEntries");
    Dump("TargetEntriesBefore");
    Dump("TargetEntriesAfter");
    Dump("AddedTextureEntries");
    Dump("PatchedTextureEntries");
    Dump("ReplacedInvalidChunks");
    Dump("AddedChunks");
    Dump("VerifiedChunks");
    Dump("VerificationFailures");
    Dump("UsedTransactionalSwap");
    Dump("TransactionDirectory");
    return;
}

Console.WriteLine($"Unknown mode: {mode}");
