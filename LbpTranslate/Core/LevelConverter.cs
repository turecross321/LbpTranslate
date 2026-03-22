namespace LbpTranslate.Core;

public record LevelConverterOptions
{
    public required string             LevelPath;
    public required ConversionSettings Settings;
    public required int                TargetRevision;

    /// <summary>
    /// Controls how the level is read from and written to disk.
    /// Defaults to <see cref="JsonLevelSerializer"/> (toolkit JSON format).
    /// Use <see cref="BinaryLevelSerializer"/> to work directly with binary .lvl files.
    /// </summary>
    public ILevelSerializer Serializer { get; init; } = new JsonLevelSerializer();

    /// <summary>
    /// When set, the converter will generate standalone plan files for each
    /// eligible PEmitter and wire the emitter to reference the plan by SHA1.
    /// Leave null to skip plan generation entirely.
    /// </summary>
    public PlanGeneratorOptions? PlanGeneration { get; init; }
}

/// <summary>
/// Represents the full output of a level conversion — the level itself plus any
/// generated plan files that must be placed alongside it.
/// </summary>
public record LevelConverterResult
{
    public required IReadOnlyList<uint> UnresolvedGuids;

    /// <summary>
    /// Suggested output folder path (sibling of the input file, named after the
    /// input stem and target game).  Write the level to
    /// <c>{SuggestedOutputFolder}/{LevelFileName}</c> and each plan binary to
    /// <c>{SuggestedOutputFolder}/{sha1}.plan</c>.
    /// </summary>
    public required string SuggestedOutputFolder;

    /// <summary>Suggested filename for the level file within the output folder.</summary>
    public required string LevelFileName;

    /// <summary>
    /// The converted level.  Serialise it via <see cref="ILevelSerializer.Serialize"/>
    /// — the library never writes files on your behalf.
    /// </summary>
    public required LbpLevel Level;

    /// <summary>
    /// Non-null when plan generation was requested.  Each entry is a generated
    /// plan binary keyed by its lowercase SHA1 hex string.  Write each as
    /// <c>{sha1}.plan</c> inside <see cref="SuggestedOutputFolder"/>.
    /// </summary>
    public PlanGeneratorResult? PlanResult { get; init; }
}

public static class LevelConverter
{
    public static LevelConverterResult Convert(LevelConverterOptions options, Action<string>? log = null)
    {
        LbpLevel level = options.Serializer.Deserialize(options.LevelPath);
        level.SetRevision(options.TargetRevision);
        level.TranslateGuids(options.Settings);

        PlanGeneratorResult? planResult = null;
        if (options.PlanGeneration != null)
            planResult = level.GeneratePlans(options.PlanGeneration);

        string folder   = BuildOutputFolder(options.LevelPath, options.Settings.ToGame);
        string fileName = Path.GetFileName(options.LevelPath);

        return new LevelConverterResult
        {
            UnresolvedGuids       = level.GetUnknownGuids(),
            SuggestedOutputFolder = folder,
            LevelFileName         = fileName,
            Level                 = level,
            PlanResult            = planResult,
        };
    }

    private static string BuildOutputFolder(string levelPath, string toGame)
    {
        string dir  = Path.GetDirectoryName(levelPath) ?? ".";
        string stem = Path.GetFileNameWithoutExtension(levelPath);
        string safe = toGame.Replace(" ", "_");
        return Path.Combine(dir, $"{stem}_{safe}");
    }
}