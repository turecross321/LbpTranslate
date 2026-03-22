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

    /// <summary>Path to the source game's data directory used to resolve .plan binaries for inventory type detection.</summary>
    public required string PlanDataDir;

    /// <summary>
    /// Override how the builder locates a plan file on disk.
    /// Defaults to a recursive <see cref="Directory.EnumerateFiles"/> search.
    /// Inject a custom resolver when building a GUI that lets the user browse.
    /// </summary>
    public Func<string /*directory*/, string /*filename*/, string? /*fullPath*/>? PlanFileResolver;
}

/// <summary>
/// Summary of one asset entry produced during settings generation.
/// Carries enough information for a GUI to render a review table or diff view.
/// </summary>
public record AssetEntry
{
    public required uint          FromGuid;
    public required uint?         ToGuid;
    public required string        FromPath;
    public required string?       ToPath;
    public required AssetCategory Category;
    public required bool          Matched;
}

public record SettingsBuilderResult
{
    public required ConversionSettings        Settings;
    public required IReadOnlyList<AssetEntry> Entries;

    public int MatchedCount   => Entries.Count(e => e.Matched);
    public int UnmatchedCount => Entries.Count(e => !e.Matched);

    /// <summary>
    /// All distinct categories that appear among unmatched assets, in order.
    /// Both the CLI and a GUI can iterate this to prompt for per-category
    /// fallback guids without reimplementing the query themselves.
    /// </summary>
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

    /// <param name="fileResolver">
    /// Optional override for locating a plan file given (directory, filename).
    /// Leave null to use the default recursive directory search.
    /// </param>
    public SettingsBuilder(Func<string, string, string?>? fileResolver = null)
    {
        _fileResolver = fileResolver ?? DefaultFileResolver;
    }

    /// <summary>
    /// Builds a <see cref="ConversionSettings"/> by cross-referencing two bluray guid maps.
    /// Includes .plan files (with inventory-type-based categories) as well as other
    /// asset types: .gmat, .mol/.msh, .tex, .anim, .smh, .mat, .bev, .pal, .ff/.fsh.
    /// Progress is reported via <paramref name="log"/> so the caller controls display.
    /// </summary>
    public SettingsBuilderResult Build(
        SettingsBuilderOptions options,
        Action<string>? log = null)
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
            GuidMap     = new Dictionary<uint, LbpAsset>(),
            DefaultGuid = null,
        };

        List<AssetEntry> entries = [];

        // ── Pass 1: .plan files (need binary parse for inventory type) ────
        foreach (FileDbEntry entry in fromDb.Entries.Where(e => e.Path.EndsWith(".plan")))
        {
            ProcessEntry(entry, toDb, options, settings, entries, log, isPlan: true);
        }

        // ── Pass 2: other mappable asset types ────────────────────────────
        foreach (FileDbEntry entry in fromDb.Entries.Where(
            e => AssetCategoryHelper.IsNonPlanMappableExtension(e.Path)))
        {
            ProcessEntry(entry, toDb, options, settings, entries, log, isPlan: false);
        }

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

        AssetCategory category;
        if (isPlan)
        {
            string fullPlanPath = Path.Combine(options.PlanDataDir, entry.Path);
            category = AssetCategoryHelper.FromPlanType(PlanReader.ReadType(fullPlanPath));
        }
        else
        {
            category = AssetCategoryHelper.FromFileExtension(entry.Path);
        }

        bool matched = match != null;

        entries.Add(new AssetEntry
        {
            FromGuid = entry.Guid,
            FromPath = entry.Path,
            ToGuid   = match?.Guid,
            ToPath   = match?.Path,
            Category = category,
            Matched  = matched,
        });

        settings.GuidMap[entry.Guid] = new LbpAsset
        {
            FromGuid = entry.Guid,
            FromPath = entry.Path,
            ToGuid   = match?.Guid,
            ToPath   = match?.Path,
            Category = category,
        };

        string status = matched ? "matched " : "no match";
        log($"  [{status}] [{category,-16}] {entry.Path}{(matched ? $" => {match!.Path}" : "")}");
    }

    private static string? DefaultFileResolver(string directory, string filename)
    {
        try
        {
            return Directory.EnumerateFiles(directory, filename, SearchOption.AllDirectories)
                            .FirstOrDefault();
        }
        catch { return null; }
    }
}