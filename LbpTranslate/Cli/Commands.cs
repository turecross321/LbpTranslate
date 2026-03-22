using System.Text.Json;
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
        Converters = { new JsonStringEnumConverter() }
    };

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
            Prompt.Confirm("Set per-category fallback guids for unmatched assets?", defaultYes: false))
        {
            foreach (AssetCategory cat in result.UnmatchedCategories)
            {
                string raw = Prompt.Ask($"  Default guid for [{cat}] (blank to skip)");
                if (uint.TryParse(raw, out uint guid))
                    settings.CategoryDefaults[cat] = guid;
            }
        }

        string raw2 = Prompt.Ask("Global fallback guid for anything unmatched (blank to skip)");
        if (uint.TryParse(raw2, out uint defaultGuid))
            settings.DefaultGuid = defaultGuid;

        File.WriteAllText(outPath, JsonSerializer.Serialize(settings, JsonOptions));
        Console.WriteLine($"Settings written to: {outPath}");

        return Task.CompletedTask;
    }

    public static Task ConvertLevel()
    {
        Console.WriteLine();
        Console.WriteLine("-- Convert Level --");
        Console.WriteLine();

        string settingsPath = PickSettingsFile();
        string levelPath    = Prompt.Ask("Path to level file");
        bool   isBinary     = Prompt.Confirm("Is this a binary .lvl / .bin file?", defaultYes: true);

        ILevelSerializer serializer;
        if (isBinary)
        {
            string jarPath = Path.Combine(AppContext.BaseDirectory, "jsoninator.jar");

            if (!File.Exists(jarPath))
            {
                Console.WriteLine($"Error: jsoninator.jar not found at {jarPath}");
                Console.WriteLine("Please place jsoninator.jar in the same directory as this executable.");
                return Task.CompletedTask;
            }

            serializer = new BinaryLevelSerializer(jarPath);
        }
        else
        {
            serializer = new JsonLevelSerializer();
        }

        Console.WriteLine();
        Console.WriteLine("Loading settings...");

        ConversionSettings settings = JsonSerializer.Deserialize<ConversionSettings>(
            File.ReadAllText(settingsPath), JsonOptions)
            ?? throw new Exception("Failed to deserialize settings file.");

        Console.WriteLine($"Loaded: {settings.FromGame} -> {settings.ToGame} ({settings.GuidMap.Count} mapped assets)");

        int revision = int.Parse(Prompt.Ask("Target revision", "994"));

        Console.WriteLine();

        LevelConverterResult result = LevelConverter.Convert(new LevelConverterOptions
        {
            LevelPath      = levelPath,
            Settings       = settings,
            TargetRevision = revision,
            Serializer     = serializer,
        });

        if (result.UnresolvedGuids.Count > 0)
        {
            Console.WriteLine($"Warning: {result.UnresolvedGuids.Count} unresolved guid(s):");
            foreach (uint guid in result.UnresolvedGuids)
                Console.WriteLine($"  {guid}");
        }

        Console.WriteLine();
        string outPath = result.SuggestedOutputPath;

        if (!Prompt.Confirm($"Export to {outPath}?"))
            outPath = Prompt.Ask("Enter custom output path");

        serializer.Serialize(result.Level, outPath);
        Console.WriteLine("Done!");

        return Task.CompletedTask;
    }

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
}