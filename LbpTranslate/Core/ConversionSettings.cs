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
    /// Per-category fallback guids. When a guid is unmapped (or maps to a missing target asset),
    /// the converter checks this dictionary by the source asset's category before falling back
    /// to <see cref="DefaultGuid"/>.
    ///
    /// Works for all asset categories, including non-plan types:
    ///   - AssetCategory.GfxMaterial  → default .gmat replacement guid
    ///   - AssetCategory.Mesh         → default .mol / .msh replacement guid
    ///   - AssetCategory.Texture      → default .tex replacement guid
    ///   - AssetCategory.Animation    → default .anim replacement guid
    ///   - AssetCategory.StaticMesh   → default .smh replacement guid
    ///   - AssetCategory.PhysicsMaterial → default .mat replacement guid
    ///   - AssetCategory.Bevel        → default .bev replacement guid
    ///   - AssetCategory.Palette      → default .pal replacement guid
    ///   - AssetCategory.Script       → default .ff / .fsh replacement guid
    ///
    /// Example JSON:
    ///   "CategoryDefaults": { "Texture": 12345, "GfxMaterial": 67890 }
    /// </summary>
    public Dictionary<AssetCategory, uint> CategoryDefaults = new();
}