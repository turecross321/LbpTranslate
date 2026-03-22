using System.Text.Json;
using System.Text.Json.Nodes;
using LbpTranslate.Plans;

namespace LbpTranslate.Core;

public class LbpLevel
{
    private readonly JsonNode _root;
    private readonly List<uint> _unknownGuids = [];
    public List<uint> GetUnknownGuids() => _unknownGuids;

    private static readonly Dictionary<string, AssetCategory> ResourceTypeToCategory =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["GFX_MATERIAL"]  = AssetCategory.GfxMaterial,
            ["MESH"]          = AssetCategory.Mesh,
            ["TEXTURE"]       = AssetCategory.Texture,
            ["ANIMATION"]     = AssetCategory.Animation,
            ["STATIC_MESH"]   = AssetCategory.StaticMesh,
            ["MATERIAL"]      = AssetCategory.PhysicsMaterial,
            ["BEVEL"]         = AssetCategory.Bevel,
            ["PALETTE"]       = AssetCategory.Palette,
            ["SCRIPT"]        = AssetCategory.Script,
            ["PLAN"]          = AssetCategory.Unknown,
        };

    public LbpLevel(string inputPath)
    {
        string json = File.ReadAllText(inputPath);
        this._root = JsonNode.Parse(json, null, new JsonDocumentOptions { MaxDepth = 256 })!;
    }

    public void SetRevision(int revision) => _root["revision"] = revision;

    public void Export(string output)
    {
        File.WriteAllText(output, _root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            MaxDepth      = 256,
        }));
    }

    public void TranslateGuids(ConversionSettings settings) => ReplaceGuids(_root, settings, _unknownGuids);

    public PlanGeneratorResult GeneratePlans(PlanGeneratorOptions options)
        => PlanGenerator.Generate(_root, options, _unknownGuids);

    /// <summary>
    /// Replaces all asset references in <paramref name="node"/> according to
    /// <paramref name="settings"/>.  Reads both legacy uint values and the new
    /// "g&lt;uint&gt;" / SHA1 string forms.  Writes resolved values as raw uint
    /// numbers or SHA1 strings (the forms the game engine understands).
    /// SHA1 values already in the JSON are left untouched.
    /// </summary>
    internal static void ReplaceGuids(JsonNode node, ConversionSettings settings, List<uint> unknownGuids)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                // ── Case 1: planGUID field ───────────────────────────────
                if (obj.ContainsKey("planGUID") &&
                    AssetRef.TryParse(obj["planGUID"], out AssetRef pgRef) &&
                    pgRef.IsGuid)
                {
                    AssetRef? resolved = ResolveKey(pgRef, null, settings, unknownGuids);
                    if (resolved.HasValue && resolved.Value.IsGuid)
                        obj["planGUID"] = resolved.Value.ToJsonNode();
                }

                // ── Case 2: { "value": <ref>, "type": "<TYPE>" } ────────
                // PLAN-typed refs are skipped: matched plans are handled by planGUID (Case 1),
                // and unmatched readymades are patched to SHA1 by PlanGenerator afterwards.
                if (obj.ContainsKey("value") && obj.ContainsKey("type") &&
                    AssetRef.TryParse(obj["value"], out AssetRef vRef) &&
                    vRef.IsGuid && vRef.Guid != 0)
                {
                    string? typeName = obj["type"] is JsonValue tv && tv.TryGetValue(out string? s) ? s : null;
                    if (typeName != null && typeName != "PLAN" &&
                        ResourceTypeToCategory.TryGetValue(typeName, out AssetCategory hintCat))
                    {
                        AssetRef? resolved = ResolveKey(vRef, hintCat, settings, unknownGuids);
                        if (resolved.HasValue)
                            obj["value"] = resolved.Value.ToJsonNode();
                    }
                }

                foreach ((string _, JsonNode? child) in obj.ToList())
                    if (child != null) ReplaceGuids(child, settings, unknownGuids);
                break;
            }
            case JsonArray arr:
            {
                foreach (JsonNode? item in arr)
                    if (item != null) ReplaceGuids(item, settings, unknownGuids);
                break;
            }
        }
    }

    /// <summary>
    /// Resolves a source asset key to its target <see cref="AssetRef"/>.
    /// Supports both uint GUID and SHA1 hash source keys.
    /// Consults GuidMap → CategoryDefaults → DefaultKey in order.
    /// </summary>
    internal static AssetRef? ResolveKey(
        AssetRef           sourceKey,
        AssetCategory?     hintCategory,
        ConversionSettings settings,
        List<uint>         unknownGuids)
    {
        // SHA1 source keys pass through unchanged — they're already target-game refs
        if (sourceKey.IsSha1)
            return sourceKey;

        uint guid = sourceKey.Guid!.Value;

        if (!settings.AssetsMap.TryGetValue(guid, out LbpAsset? asset))
        {
            if (!unknownGuids.Contains(guid)) unknownGuids.Add(guid);
            AssetCategory cat = hintCategory ?? AssetCategory.Unknown;
            AssetRef? fallback = settings.CategoryDefaults.TryGetValue(cat, out AssetRef cd)
                ? cd
                : settings.DefaultKey;
            Console.WriteLine($"  [not in map] [{cat,-16}] guid {guid} => fallback {fallback?.ToString() ?? "null"}");
            return fallback;
        }

        if (asset.ToKey != null)
            return asset.ToKey.Value;

        // In map but no target
        AssetRef? result = settings.CategoryDefaults.TryGetValue(asset.Category, out AssetRef catDef)
            ? catDef
            : settings.DefaultKey;
        Console.WriteLine($"  [no target ] [{asset.Category,-16}] guid {guid} ({asset.FromPath}) => fallback {result?.ToString() ?? "null"}");
        return result;
    }

    // Kept for PlanGenerator which passes its own unknownGuids list
    internal static void ReplaceGuids(JsonNode node, ConversionSettings settings, List<uint> unknownGuids,
        bool _unused) => ReplaceGuids(node, settings, unknownGuids);
}