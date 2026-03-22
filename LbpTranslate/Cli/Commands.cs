using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using LbpTranslate.Core;
using LbpTranslate.Plans;

namespace LbpTranslate.Cli;

public static class Commands
{
    private const string SettingsFolder = "settings";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        IncludeFields = true,
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter() }
    };

    // ── Generate Settings ────────────────────────────────────────────────────

    public static Task GenerateSettings()
    {
        Console.WriteLine();
        Console.WriteLine("-- Generate Settings --");
        Console.WriteLine();

        string fromGame = Prompt.Ask("Source game name (e.g. LBP2)");
        string toGame   = Prompt.Ask("Target game name (e.g. LBP Vita)");
        string fromMap  = Prompt.Ask($"Path to {fromGame} blurayguids.map");
        string toMap    = Prompt.Ask($"Path to {toGame} blurayguids.map");
        string planDir  = Prompt.Ask("Path to source game data directory");

        Directory.CreateDirectory(SettingsFolder);
        string defaultOut = Path.Combine(SettingsFolder, $"{fromGame}_to_{toGame}.json");
        string outPath    = Prompt.Ask("Output settings file path", defaultOut);

        Console.WriteLine();

        SettingsBuilderResult result = new SettingsBuilder().Build(
            new SettingsBuilderOptions
            {
                FromGame    = fromGame,
                ToGame      = toGame,
                FromMapPath = fromMap,
                ToMapPath   = toMap,
                PlanDataDir = planDir,
            },
            log: Console.WriteLine
        );

        Console.WriteLine();
        Console.WriteLine($"Matched: {result.MatchedCount}, Unmatched: {result.UnmatchedCount}");

        ConversionSettings settings = result.Settings;

        if (result.UnmatchedCategories.Count > 0 &&
            Prompt.Confirm("Set per-category fallback asset keys for unmatched assets?", defaultYes: false))
        {
            foreach (AssetCategory cat in result.UnmatchedCategories)
            {
                string raw = Prompt.Ask($"  Default key for [{cat}] (g<guid> or sha1, blank to skip)");
                if (AssetRef.TryParseSettingsString(raw, out AssetRef key))
                    settings.CategoryDefaults[cat] = key;
            }
        }

        string raw2 = Prompt.Ask("Global fallback asset key (g<guid> or sha1, blank to skip)");
        if (AssetRef.TryParseSettingsString(raw2, out AssetRef defaultKey))
            settings.DefaultKey = defaultKey;

        File.WriteAllText(outPath, JsonSerializer.Serialize(settings, JsonOptions));
        Console.WriteLine($"Settings written to: {outPath}");

        return Task.CompletedTask;
    }

    // ── Convert Level ────────────────────────────────────────────────────────

    public static Task ConvertLevel()
    {
        Console.WriteLine();
        Console.WriteLine("-- Convert Level --");
        Console.WriteLine();

        string settingsPath = PickSettingsFile();
        string levelPath    = Prompt.Ask("Path to level file");
        bool   isBinary     = Prompt.Confirm("Is this a binary .lvl / .bin file?", defaultYes: true);

        if (!Jsoninator.TryFindJar(out string jarPath))
        {
            Console.WriteLine($"Error: jsoninator.jar not found at {jarPath}");
            Console.WriteLine("Please place jsoninator.jar in the same directory as this executable.");
            return Task.CompletedTask;
        }

        int heapMb     = AskHeapMb();
        var jsoninator = new Jsoninator(jarPath, Console.WriteLine) { MaxHeapMb = heapMb };

        ILevelSerializer serializer = isBinary
            ? new BinaryLevelSerializer(jarPath, Console.WriteLine, heapMb)
            : new JsonLevelSerializer();

        Console.WriteLine();
        Console.WriteLine("Loading settings...");

        ConversionSettings settings = JsonSerializer.Deserialize<ConversionSettings>(
            File.ReadAllText(settingsPath), JsonOptions)
            ?? throw new Exception("Failed to deserialize settings file.");

        Console.WriteLine($"Loaded: {settings.FromGame} -> {settings.ToGame} " +
                          $"({settings.AssetsMap.Count} mapped assets)");

        int revision = int.Parse(Prompt.Ask("Target revision", "994"));

        // ── Plan generation ───────────────────────────────────────────────────
        // Emitted readymade objects that have no match in the settings map must
        // be converted on-the-fly to community (hash-referenced) plan files.
        // This always happens; we just ask where the source plan binaries live.
        Console.WriteLine();
        Console.WriteLine("Plan generation converts readymade objects emitted by emitters");
        Console.WriteLine("that have no matching entry in the target game into standalone");
        Console.WriteLine("community plan files referenced by SHA1 hash.");
        Console.WriteLine();

        string planBinaryDir = Prompt.Ask(
            "Path to source game data directory (for emitted plan files)");

        PlanGeneratorOptions planOptions = BuildPlanGeneratorOptions(
            planBinaryDir, settings, jsoninator);

        Console.WriteLine();

        LevelConverterResult result = LevelConverter.Convert(new LevelConverterOptions
        {
            LevelPath      = levelPath,
            Settings       = settings,
            TargetRevision = revision,
            Serializer     = serializer,
            PlanGeneration = planOptions,
        }, log: Console.WriteLine);

        // ── Plan generation summary ───────────────────────────────────────────
        if (result.PlanResult is { } pr)
        {
            Console.WriteLine($"Plan generation: {pr.GeneratedCount} generated, " +
                              $"{pr.SkippedCount} skipped (not found on disk).");
            if (pr.UnresolvedPlanGuids.Count > 0)
            {
                Console.WriteLine($"  {pr.UnresolvedPlanGuids.Count} plan(s) not found locally:");
                foreach (uint guid in pr.UnresolvedPlanGuids)
                    Console.WriteLine($"    guid {guid}");
            }
        }

        // ── Unresolved GUID summary ───────────────────────────────────────────
        if (result.UnresolvedGuids.Count > 0)
        {
            Console.WriteLine($"Warning: {result.UnresolvedGuids.Count} unresolved guid(s):");
            foreach (uint guid in result.UnresolvedGuids)
                Console.WriteLine($"  {guid}");
        }

        // ── Output ────────────────────────────────────────────────────────────
        Console.WriteLine();
        string outFolder = result.SuggestedOutputFolder;

        if (!Prompt.Confirm($"Export to folder '{outFolder}'?"))
            outFolder = Prompt.Ask("Enter custom output folder path");

        Directory.CreateDirectory(outFolder);

        string levelOutPath = Path.Combine(outFolder, result.LevelFileName);
        serializer.Serialize(result.Level, levelOutPath);
        Console.WriteLine($"Level written to: {levelOutPath}");

        if (result.PlanResult is { } planResult && planResult.GeneratedPlans.Count > 0)
        {
            foreach ((string sha1, byte[] bytes) in planResult.GeneratedPlans)
            {
                string planPath = Path.Combine(outFolder, sha1);
                File.WriteAllBytes(planPath, bytes);
                Console.WriteLine($"Plan written to:  {planPath}");
            }
        }

        Console.WriteLine("Done!");
        return Task.CompletedTask;
    }

    // ── Convert Plans to JSON ────────────────────────────────────────────────

    public static Task ConvertPlans()
    {
        Console.WriteLine();
        Console.WriteLine("-- Convert Plans to JSON --");
        Console.WriteLine();

        if (!Jsoninator.TryFindJar(out string jarPath))
        {
            Console.WriteLine($"Error: jsoninator.jar not found at {jarPath}");
            Console.WriteLine("Please place jsoninator.jar in the same directory as this executable.");
            return Task.CompletedTask;
        }

        string sourceDir = Prompt.Ask("Path to source game data directory");
        string outputDir = Prompt.Ask("Output directory for JSON files");
        bool overwrite   = Prompt.Confirm("Overwrite existing JSON files?", defaultYes: false);

        string[] planFiles = Directory.GetFiles(sourceDir, "*.plan", SearchOption.AllDirectories);
        Console.WriteLine();
        Console.WriteLine($"Found {planFiles.Length} plan file(s). Converting...");
        Console.WriteLine();

        var jsoninator = new Jsoninator(jarPath, Console.WriteLine) { MaxHeapMb = AskHeapMb() };

        int done = 0, skipped = 0, failed = 0;

        foreach (string planPath in planFiles)
        {
            string relative   = Path.GetRelativePath(sourceDir, planPath);
            string outputPath = Path.Combine(outputDir, Path.ChangeExtension(relative, ".json"));

            if (!overwrite && File.Exists(outputPath)) { skipped++; continue; }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            try
            {
                jsoninator.Convert(planPath, outputPath);
                Console.WriteLine($"  [ok     ] {relative}");
                done++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [failed ] {relative} — {ex.Message}");
                failed++;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Done. Converted: {done}, Skipped: {skipped}, Failed: {failed}");
        return Task.CompletedTask;
    }

    // ── Lookup GUID ──────────────────────────────────────────────────────────

    public static Task LookupGuid()
    {
        Console.WriteLine();
        Console.WriteLine("-- Lookup GUID --");
        Console.WriteLine();

        string mapPath = Prompt.Ask("Path to blurayguids.map");

        Console.WriteLine("Loading...");
        BluRayGuids.FileDb db = BluRayGuids.FileDb.Load(mapPath);
        Console.WriteLine($"Loaded {db.Entries.Count} entries.");
        Console.WriteLine();

        while (true)
        {
            string raw = Prompt.Ask("GUID to look up (blank to exit)");
            if (string.IsNullOrEmpty(raw)) break;

            if (!uint.TryParse(raw, out uint guid))
            {
                Console.WriteLine("Invalid GUID — enter a numeric value.");
                continue;
            }

            if (db.Lookup.TryGetValue(guid, out BluRayGuids.FileDbEntry? entry))
                Console.WriteLine($"  => {entry.Path}");
            else
                Console.WriteLine("  (not found)");

            Console.WriteLine();
        }

        return Task.CompletedTask;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static int AskHeapMb()
    {
        string raw = Prompt.Ask("Max JVM heap size for jsoninator (MB)", "256");
        return int.TryParse(raw, out int mb) && mb > 0 ? mb : 64;
    }

    private static string PickSettingsFile()
    {
        string[] found = Directory.Exists(SettingsFolder)
            ? Directory.GetFiles(SettingsFolder, "*.json")
            : [];

        if (found.Length == 0)
            return Prompt.Ask("Path to settings file");

        string[] options = [..found.Select(Path.GetFileName)!, "Enter path manually"];
        int choice = Prompt.Menu("Select a settings file:", options);

        return choice == found.Length
            ? Prompt.Ask("Path to settings file")
            : found[choice];
    }

    // ── PlanGenerator wiring ─────────────────────────────────────────────────

    private static PlanGeneratorOptions BuildPlanGeneratorOptions(
        string             planBinaryDir,
        ConversionSettings settings,
        Jsoninator         jsoninator)
    {
        // Reverse map: if a plan was matched and the target key is a GUID,
        // we can still find the source binary for it (needed when a plan
        // internally references another plan by its target GUID).
        var reverseMap = new Dictionary<uint, LbpAsset>();
        foreach (LbpAsset asset in settings.AssetsMap.Values)
        {
            if (asset.ToKey is { } tk && tk.IsGuid && asset.FromPath != null &&
                asset.FromPath.EndsWith(".plan", StringComparison.OrdinalIgnoreCase))
                reverseMap.TryAdd(tk.Guid!.Value, asset);
        }

        var loadCache      = new Dictionary<uint, JsonObject?>();
        var serializeCache = new Dictionary<string, byte[]?>();

        return new PlanGeneratorOptions
        {
            Settings = settings,

            PlanLoader = guid =>
            {
                if (loadCache.TryGetValue(guid, out JsonObject? cached)) return cached;
                JsonObject? r = TryLoadPlan(guid, planBinaryDir, settings, reverseMap, jsoninator);
                loadCache[guid] = r;
                return r;
            },

            PlanSerializer = planJson =>
            {
                string cacheKey = planJson.ToJsonString();
                if (serializeCache.TryGetValue(cacheKey, out byte[]? cached)) return cached;
                byte[]? r = TrySerializePlan(planJson, jsoninator);
                serializeCache[cacheKey] = r;
                return r;
            },
        };
    }

    private static JsonObject? TryLoadPlan(
        uint                       guid,
        string                     planBinaryDir,
        ConversionSettings         settings,
        Dictionary<uint, LbpAsset> reverseMap,
        Jsoninator                 jsoninator)
    {
        LbpAsset? asset =
            settings.AssetsMap.TryGetValue(guid, out LbpAsset? fwd) ? fwd :
            reverseMap.TryGetValue(guid, out LbpAsset? rev)         ? rev :
            null;

        if (asset?.FromPath == null) return null;

        string fullPath = Path.Combine(planBinaryDir, asset.FromPath);
        if (File.Exists(fullPath))
            return jsoninator.ConvertPlanToJson(fullPath);

        string filename = Path.GetFileName(asset.FromPath);
        try
        {
            string? found = Directory
                .EnumerateFiles(planBinaryDir, filename, SearchOption.AllDirectories)
                .FirstOrDefault();
            if (found != null)
                return jsoninator.ConvertPlanToJson(found);
        }
        catch { }

        return null;
    }

    private static byte[]? TrySerializePlan(JsonObject planJson, Jsoninator jsoninator)
    {
        string tempJson = Path.ChangeExtension(Path.GetTempFileName(), ".json");
        string tempBin  = Path.ChangeExtension(Path.GetTempFileName(), ".plan");
        try
        {
            File.WriteAllText(tempJson, planJson.ToJsonString(
                new JsonSerializerOptions { WriteIndented = true, MaxDepth = 256 }));
            jsoninator.Convert(tempJson, tempBin);
            return File.ReadAllBytes(tempBin);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [plan serialize failed] {ex.Message}");
            return null;
        }
        finally
        {
            if (File.Exists(tempJson)) File.Delete(tempJson);
            if (File.Exists(tempBin))  File.Delete(tempBin);
        }
    }
}