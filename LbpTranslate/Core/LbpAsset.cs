using System.Text.Json.Serialization;
using LbpTranslate.Plans;

namespace LbpTranslate.Core;

/// <summary>
/// Maps a single asset from one game's bluray guid map to another.
/// Both the source key and target key can be either a uint GUID or a SHA1 hash —
/// every asset in LBP can be referenced by either form.
/// </summary>
public class LbpAsset
{
    /// <summary>Source-game asset key (always a uint GUID from the bluray map).</summary>
    [JsonConverter(typeof(AssetKeyJsonConverter))]
    public required AssetRef FromKey;

    /// <summary>Target-game asset key, or null if no match was found.</summary>
    [JsonConverter(typeof(AssetKeyJsonConverter))]
    public required AssetRef? ToKey;

    public required string? FromPath;
    public required string? ToPath;

    public AssetCategory Category;
}