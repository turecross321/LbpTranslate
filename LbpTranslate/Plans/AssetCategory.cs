namespace LbpTranslate.Plans;

/// <summary>
/// High-level category of an LBP asset, derived from its file extension
/// and (for .plan files) its InventoryObjectType flags.
/// </summary>
public enum AssetCategory
{
    Unknown,

    // ── Plan inventory types ──────────────────────────────────────────────
    Material,       // PRIMITIVE_MATERIAL or COSTUME_MATERIAL
    Sticker,        // STICKER / USER_STICKER
    Decoration,     // DECORATION
    Costume,        // COSTUME
    Background,     // BACKGROUND
    GameplayKit,    // GAMEPLAY_KIT
    Sound,          // SOUND / MUSIC / INSTRUMENT
    Gadget,         // GADGET
    Readymade,      // READYMADE
    Joint,          // JOINT
    UserObject,     // USER_OBJECT
    PrimitiveShape, // PRIMITIVE_SHAPE
    Sequencer,      // SEQUENCER
    Danger,         // DANGER
    Eyetoy,         // EYETOY
    Tool,           // TOOL
    SackbotMesh,    // SACKBOT_MESH

    // ── Non-plan file types ───────────────────────────────────────────────
    GfxMaterial,    // .gmat / .gmt  — GFX_MATERIAL (GMT)
    Mesh,           // .mol / .msh   — MESH (MSH)
    Texture,        // .tex          — TEXTURE (TEX) / GTF_TEXTURE (GTF)
    Animation,      // .anim / .anm  — ANIMATION (ANM)
    StaticMesh,     // .smh          — STATIC_MESH (SMH)
    PhysicsMaterial,// .mat          — MATERIAL (MAT) — physics, distinct from plan Material
    Bevel,          // .bev          — BEVEL (BEV)
    Palette,        // .pal          — PALETTE (PAL)
    Script,         // .ff / .fsh    — SCRIPT (FSH)
}

public static class AssetCategoryHelper
{
    /// <summary>
    /// Returns the category for a .plan file based on its parsed InventoryObjectType.
    /// </summary>
    public static AssetCategory FromPlanType(InventoryObjectType type)
    {
        // Check in priority order — flags can overlap so most-specific wins
        if (type.HasFlag(InventoryObjectType.PrimitiveMaterial) ||
            type.HasFlag(InventoryObjectType.CostumeMaterial))
            return AssetCategory.Material;

        if (type.HasFlag(InventoryObjectType.Sticker) ||
            type.HasFlag(InventoryObjectType.UserSticker))
            return AssetCategory.Sticker;

        if (type.HasFlag(InventoryObjectType.Decoration))
            return AssetCategory.Decoration;

        if (type.HasFlag(InventoryObjectType.Costume))
            return AssetCategory.Costume;

        if (type.HasFlag(InventoryObjectType.Background))
            return AssetCategory.Background;

        if (type.HasFlag(InventoryObjectType.GameplayKit))
            return AssetCategory.GameplayKit;

        if (type.HasFlag(InventoryObjectType.Sound)  ||
            type.HasFlag(InventoryObjectType.Music)  ||
            type.HasFlag(InventoryObjectType.Instrument))
            return AssetCategory.Sound;

        if (type.HasFlag(InventoryObjectType.Gadget))
            return AssetCategory.Gadget;

        if (type.HasFlag(InventoryObjectType.Readymade))
            return AssetCategory.Readymade;

        if (type.HasFlag(InventoryObjectType.Joint))
            return AssetCategory.Joint;

        if (type.HasFlag(InventoryObjectType.UserObject))
            return AssetCategory.UserObject;

        if (type.HasFlag(InventoryObjectType.PrimitiveShape))
            return AssetCategory.PrimitiveShape;

        if (type.HasFlag(InventoryObjectType.Sequencer))
            return AssetCategory.Sequencer;

        if (type.HasFlag(InventoryObjectType.Danger))
            return AssetCategory.Danger;

        if (type.HasFlag(InventoryObjectType.Eyetoy))
            return AssetCategory.Eyetoy;

        if (type.HasFlag(InventoryObjectType.Tool))
            return AssetCategory.Tool;

        if (type.HasFlag(InventoryObjectType.SackbotMesh))
            return AssetCategory.SackbotMesh;

        return AssetCategory.Unknown;
    }

    /// <summary>
    /// Returns the category for a non-plan asset based purely on its file extension.
    /// This covers textures, meshes, gfx materials, animations, etc.
    /// The extension comparison is case-insensitive and must include the leading dot.
    /// </summary>
    public static AssetCategory FromFileExtension(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".gmat" or ".gmt"  => AssetCategory.GfxMaterial,
            ".mol"  or ".msh"  => AssetCategory.Mesh,
            ".tex"             => AssetCategory.Texture,
            ".anim" or ".anm"  => AssetCategory.Animation,
            ".smh"             => AssetCategory.StaticMesh,
            ".mat"             => AssetCategory.PhysicsMaterial,
            ".bev"             => AssetCategory.Bevel,
            ".pal"             => AssetCategory.Palette,
            ".ff"   or ".fsh"  => AssetCategory.Script,
            _                  => AssetCategory.Unknown,
        };
    }

    /// <summary>
    /// Returns true for extensions that should be included in the guid map
    /// but are not .plan files (i.e. handled by <see cref="FromFileExtension"/>).
    /// </summary>
    public static bool IsNonPlanMappableExtension(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".gmat" or ".gmt"
                   or ".mol"  or ".msh"
                   or ".tex"
                   or ".anim" or ".anm"
                   or ".smh"
                   or ".mat"
                   or ".bev"
                   or ".pal"
                   or ".ff"   or ".fsh";
    }
}