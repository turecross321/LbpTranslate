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
}

public record LevelConverterResult
{
    public required IReadOnlyList<uint> UnresolvedGuids;
    public required string             SuggestedOutputPath;

    /// <summary>
    /// The converted level. Call <see cref="LbpLevel.Export"/> with your chosen
    /// output path to write it — the library never writes files on your behalf.
    /// </summary>
    public required LbpLevel Level;
}

public static class LevelConverter
{
    /// <summary>
    /// Loads, patches, and translates a level file.
    /// Never writes to disk — the caller decides where to export.
    /// </summary>
    public static LevelConverterResult Convert(LevelConverterOptions options)
    {
        LbpLevel level = options.Serializer.Deserialize(options.LevelPath);
        level.SetRevision(options.TargetRevision);
        level.TranslateGuids(options.Settings);

        string suggestedOutput = BuildOutputPath(options.LevelPath, options.Settings.ToGame);

        return new LevelConverterResult
        {
            UnresolvedGuids     = level.GetUnknownGuids(),
            SuggestedOutputPath = suggestedOutput,
            Level               = level,
        };
    }

    private static string BuildOutputPath(string levelPath, string toGame) =>
        Path.Combine(
            Path.GetDirectoryName(levelPath) ?? ".",
            Path.GetFileNameWithoutExtension(levelPath) +
            $"_{toGame.Replace(" ", "_")}" +
            Path.GetExtension(levelPath)
        );
}