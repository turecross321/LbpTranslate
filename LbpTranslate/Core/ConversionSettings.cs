using LbpTranslate.Plans;

namespace LbpTranslate.Core;

public class ConversionSettings
{
    /// <summary>The source game these assets originate from (e.g. "LBP2").</summary>
    public required string FromGame;

    /// <summary>The target game these assets are being translated to (e.g. "LBP Vita").</summary>
    public required string ToGame;

    public required Dictionary<uint, LbpAsset> GuidMap;

    /// <summary>Fallback guid used when no mapping is found and no category default applies.</summary>
    public required uint? DefaultGuid;

    /// <summary>
    /// Per-category fallback guids. When a guid is unmapped, the converter checks
    /// this dictionary by the source asset's category before falling back to DefaultGuid.
    /// E.g. { "Material": 12345, "Sticker": 67890 }
    /// </summary>
    public Dictionary<AssetCategory, uint> CategoryDefaults = new();
}