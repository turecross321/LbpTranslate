using System.Text.Json;
using System.Text.Json.Nodes;
using LbpTranslate.Plans;

namespace LbpTranslate.Core;

public class LbpLevel
{
    private readonly JsonNode _root;
    private readonly List<uint> _unknownGuids = [];
    public List<uint> GetUnknownGuids() => _unknownGuids;

    /// <summary>
    /// Maps the "type" string found inside a resource-descriptor object
    /// (e.g. "GFX_MATERIAL", "MESH", "TEXTURE") to the corresponding
    /// AssetCategory so we can apply per-category fallbacks.
    /// </summary>
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
            // PLAN is handled separately via planGUID — skip here
        };

    public LbpLevel(string inputPath)
    {
        string json = File.ReadAllText(inputPath);
        this._root = JsonNode.Parse(json, null, new JsonDocumentOptions
        {
            MaxDepth = 256
        })!;
    }

    public void SetRevision(int revision)
    {
        _root["revision"] = revision;
    }

    public void Export(string output)
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            MaxDepth = 256
        };

        File.WriteAllText(output, _root.ToJsonString(options));
    }

    public void TranslateGuids(ConversionSettings settings)
    {
        ReplaceGuids(_root, settings);
    }

    private void ReplaceGuids(JsonNode node, ConversionSettings settings)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                // ── Case 1: planGUID  ────────────────────────────────────
                if (obj.ContainsKey("planGUID"))
                {
                    uint? oldGuid = obj["planGUID"] is JsonValue pv && pv.TryGetValue(out uint pg) ? pg : null;
                    if (oldGuid != null)
                        obj["planGUID"] = ResolveGuid((uint)oldGuid, hintCategory: null, settings);
                }

                // ── Case 2: { "value": <guid>, "type": "<RESOURCE_TYPE>" }
                // Resource-descriptor objects used for gfxMaterial, mesh, texture, etc.
                if (obj.ContainsKey("value") && obj.ContainsKey("type"))
                {
                    string? typeName = obj["type"] is JsonValue tv && tv.TryGetValue(out string? s) ? s : null;
                    uint?   oldGuid  = obj["value"] is JsonValue vv && vv.TryGetValue(out uint u) ? u : null;

                    if (typeName != null
                        && oldGuid is not null and not 0
                        && ResourceTypeToCategory.TryGetValue(typeName, out AssetCategory hintCat))
                    {
                        obj["value"] = ResolveGuid((uint)oldGuid, hintCat, settings);
                    }
                }

                // Recurse into all children regardless of which case matched above
                foreach ((string _, JsonNode? child) in obj.ToList())
                {
                    if (child != null)
                        ReplaceGuids(child, settings);
                }

                break;
            }
            case JsonArray arr:
            {
                foreach (JsonNode? item in arr)
                {
                    if (item != null)
                        ReplaceGuids(item, settings);
                }

                break;
            }
        }
    }

    /// <summary>
    /// Resolves a single guid to its target, consulting in order:
    ///   1. GuidMap (direct match)
    ///   2. CategoryDefaults (by the asset's stored category, or hint if not in map)
    ///   3. DefaultGuid
    /// </summary>
    private uint? ResolveGuid(uint oldGuid, AssetCategory? hintCategory, ConversionSettings settings)
    {
        if (!settings.GuidMap.TryGetValue(oldGuid, out LbpAsset? asset))
        {
            if (!_unknownGuids.Contains(oldGuid))
                _unknownGuids.Add(oldGuid);

            AssetCategory cat = hintCategory ?? AssetCategory.Unknown;
            uint? fallback = settings.CategoryDefaults.TryGetValue(cat, out uint cd)
                ? cd
                : settings.DefaultGuid;
            Console.WriteLine($"  [not in map] [{cat,-16}] guid {oldGuid} (unknown path) => fallback {fallback}");
            return fallback;
        }

        if (asset.ToGuid != null)
            return asset.ToGuid;

        // In map but has no matching target asset
        uint? result = settings.CategoryDefaults.TryGetValue(asset.Category, out uint catDefault)
            ? catDefault
            : settings.DefaultGuid;
        Console.WriteLine($"  [no target ] [{asset.Category,-16}] guid {oldGuid} ({asset.FromPath}) => fallback {result}");
        return result;
    }
}