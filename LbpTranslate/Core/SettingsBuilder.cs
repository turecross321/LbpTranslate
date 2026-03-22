using LbpTranslate.Plans;
using FileDb = LbpTranslate.BluRayGuids.FileDb;
using FileDbEntry = LbpTranslate.BluRayGuids.FileDbEntry;

namespace LbpTranslate.Core;

public record SettingsBuilderOptions
{
    public required string FromGame;
    public required string ToGame;
    public required string FromMapPath;
    public required string ToMapPath;
    public required string PlanDataDir;
    public Func<string, string, string?>? PlanFileResolver;
}

public record AssetEntry
{
    public required AssetRef        FromKey;
    public required AssetRef?       ToKey;
    public required string          FromPath;
    public required string?         ToPath;
    public required AssetCategory   Category;
    public required bool            Matched;

    // Convenience
    public uint? FromGuid => FromKey.IsGuid ? FromKey.Guid : null;
    public uint? ToGuid   => ToKey?.IsGuid == true ? ToKey.Value.Guid : null;
}

public record SettingsBuilderResult
{
    public required ConversionSettings        Settings;
    public required IReadOnlyList<AssetEntry> Entries;

    public int MatchedCount   => Entries.Count(e => e.Matched);
    public int UnmatchedCount => Entries.Count(e => !e.Matched);

    public IReadOnlyList<AssetCategory> UnmatchedCategories =>
        Entries.Where(e => !e.Matched)
               .Select(e => e.Category)
               .Distinct()
               .Order()
               .ToList();
}

public class SettingsBuilder
{
    private readonly Func<string, string, string?> _fileResolver;

    public SettingsBuilder(Func<string, string, string?>? fileResolver = null)
    {
        _fileResolver = fileResolver ?? DefaultFileResolver;
    }

    public SettingsBuilderResult Build(SettingsBuilderOptions options, Action<string>? log = null)
    {
        log ??= _ => { };

        log($"Loading {options.FromGame} guid map...");
        FileDb fromDb = FileDb.Load(options.FromMapPath);

        log($"Loading {options.ToGame} guid map...");
        FileDb toDb = FileDb.Load(options.ToMapPath);

        ConversionSettings settings = new()
        {
            FromGame    = options.FromGame,
            ToGame      = options.ToGame,
            AssetsMap     = new Dictionary<uint, LbpAsset>(),
            DefaultKey  = null,
        };

        List<AssetEntry> entries = [];

        foreach (FileDbEntry entry in fromDb.Entries.Where(e => e.Path.EndsWith(".plan")))
            ProcessEntry(entry, toDb, options, settings, entries, log, isPlan: true);

        foreach (FileDbEntry entry in fromDb.Entries.Where(
            e => AssetCategoryHelper.IsNonPlanMappableExtension(e.Path)))
            ProcessEntry(entry, toDb, options, settings, entries, log, isPlan: false);

        return new SettingsBuilderResult { Settings = settings, Entries = entries };
    }

    private void ProcessEntry(
        FileDbEntry            entry,
        FileDb                 toDb,
        SettingsBuilderOptions options,
        ConversionSettings     settings,
        List<AssetEntry>       entries,
        Action<string>         log,
        bool                   isPlan)
    {
        string       filename = entry.Path.Split("/").Last();
        FileDbEntry? match    = toDb.Entries.FirstOrDefault(e => e.Path.Contains(filename));

        AssetCategory category = isPlan
            ? AssetCategoryHelper.FromPlanType(
                PlanReader.ReadType(Path.Combine(options.PlanDataDir, entry.Path)))
            : AssetCategoryHelper.FromFileExtension(entry.Path);

        bool matched = match != null;

        var fromKey = AssetRef.FromGuid(entry.Guid);
        var toKey   = match != null ? AssetRef.FromGuid(match.Guid) : (AssetRef?)null;

        entries.Add(new AssetEntry
        {
            FromKey  = fromKey,
            ToKey    = toKey,
            FromPath = entry.Path,
            ToPath   = match?.Path,
            Category = category,
            Matched  = matched,
        });

        settings.AssetsMap[entry.Guid] = new LbpAsset
        {
            FromKey  = fromKey,
            ToKey    = toKey,
            FromPath = entry.Path,
            ToPath   = match?.Path,
            Category = category,
        };

        string status = matched ? "matched " : "no match";
        log($"  [{status}] [{category,-16}] {entry.Path}{(matched ? $" => {match!.Path}" : "")}");
    }

    private static string? DefaultFileResolver(string directory, string filename)
    {
        try { return Directory.EnumerateFiles(directory, filename, SearchOption.AllDirectories).FirstOrDefault(); }
        catch { return null; }
    }
}