using System.Text.Json;
using System.Text.Json.Serialization;
using LbpTranslate.Core;
using LbpTranslate.Plans;

namespace LbpTranslate.Cli;

public static class Commands
{
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

        string  fromGame = Prompt.Ask("Source game name (e.g. LBP2)");
        string  toGame   = Prompt.Ask("Target game name (e.g. LBP Vita)");
        string  fromMap  = Prompt.Ask($"Path to {fromGame} blurayguids.map");
        string  toMap    = Prompt.Ask($"Path to {toGame} blurayguids.map");
        string planDir  = Prompt.Ask("Path to source game data directory");
        string outPath   = Prompt.Ask("Output settings file path", $"{fromGame}_to_{toGame}.json");

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

        // Per-category fallback guids — the library tells us which categories
        // have unmatched assets; we just prompt for each one.
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

        string settingsPath = Prompt.Ask("Path to settings file");
        string levelPath    = Prompt.Ask("Path to level JSON");

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

        result.Level.Export(outPath);
        Console.WriteLine("Done!");

        return Task.CompletedTask;
    }
}