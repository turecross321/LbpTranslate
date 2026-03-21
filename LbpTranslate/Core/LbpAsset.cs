using LbpTranslate.Plans;

namespace LbpTranslate.Core;

/// <summary>Maps a single asset guid from one game's bluray guid map to another.</summary>
public class LbpAsset
{
    public required uint          FromGuid;
    public required uint?          ToGuid;
    public required string?       FromPath;
    public required string?       ToPath;

    /// <summary>
    /// High-level category of this asset (e.g. Material, Sticker, Decoration).
    /// Used to set per-category defaults in ConversionSettings.
    /// </summary>
    public AssetCategory Category;
}