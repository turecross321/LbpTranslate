using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace LbpTranslate.Core;

public record PlanGeneratorOptions
{
    /// <summary>The conversion settings used to translate internal GUIDs.</summary>
    public required ConversionSettings Settings;

    /// <summary>
    /// Loads and parses a binary .plan file by its source GUID.
    /// Returns null if the plan cannot be found or parsed.
    /// </summary>
    public required Func<uint, JsonObject?> PlanLoader;

    /// <summary>
    /// Serializes a finished plan JsonObject to its binary representation.
    /// Returns null if serialization fails.
    /// </summary>
    public required Func<JsonObject, byte[]?> PlanSerializer;

    /// <summary>Maximum recursion depth for nested readymade references.</summary>
    public int MaxDepth { get; init; } = 8;
}

public record PlanGeneratorResult
{
    /// <summary>
    /// All generated plan binaries keyed by their lowercase SHA1 hex string.
    /// Write each as a file named exactly by its key alongside the level.
    /// </summary>
    public required IReadOnlyDictionary<string, byte[]> GeneratedPlans;

    /// <summary>Maps source plan GUIDs to their output SHA1 hashes.</summary>
    public required IReadOnlyDictionary<uint, string> GuidToSha1;

    public int GeneratedCount => GeneratedPlans.Count;
    public int SkippedCount   => UnresolvedPlanGuids.Count;

    /// <summary>GUIDs of plans that could not be found on disk.</summary>
    public required IReadOnlyList<uint> UnresolvedPlanGuids;
}

/// <summary>
/// Converts in-game readymade plans referenced by emitters into standalone
/// community-object plan files, referenced by SHA1 hash.
///
/// The plan JSON is loaded as-is, its internal GUIDs are translated via
/// ConversionSettings, its inventoryData is minimally adjusted to look
/// user-created (based on the diff between a built-in and user-created plan),
/// and it is serialized back to binary and SHA1-hashed.
/// </summary>
public static class PlanGenerator
{
    public static PlanGeneratorResult Generate(
        JsonNode             levelRoot,
        PlanGeneratorOptions options,
        List<uint>           levelUnknownGuids)
    {
        var converted     = new Dictionary<uint, string>();   // guid → sha1
        var generatedBins = new Dictionary<string, byte[]>(); // sha1 → bytes
        var unresolved    = new List<uint>();
        var visited       = new HashSet<uint>();

        // Collect all emitter plan GUIDs from the level
        List<uint> emitterGuids = CollectEmitterPlanGuids(levelRoot).Distinct().ToList();

        foreach (uint guid in emitterGuids)
            ConvertPlan(guid, options, converted, generatedBins, unresolved, visited,
                levelUnknownGuids, depth: 0);

        // Patch the level: replace uint plan GUIDs with SHA1 strings
        if (converted.Count > 0)
            PatchPlanReferences(levelRoot, converted);

        return new PlanGeneratorResult
        {
            GeneratedPlans      = generatedBins,
            GuidToSha1          = converted,
            UnresolvedPlanGuids = unresolved,
        };
    }

    // ── Core recursive conversion ────────────────────────────────────────────

    private static void ConvertPlan(
        uint                       guid,
        PlanGeneratorOptions       options,
        Dictionary<uint, string>   converted,
        Dictionary<string, byte[]> generatedBins,
        List<uint>                 unresolved,
        HashSet<uint>              visited,
        List<uint>                 levelUnknownGuids,
        int                        depth)
    {
        if (depth > options.MaxDepth || converted.ContainsKey(guid) || visited.Contains(guid))
            return;
        visited.Add(guid);

        // Load the plan JSON from disk via jsoninator
        JsonObject? planJson = options.PlanLoader(guid);
        if (planJson == null)
        {
            if (!unresolved.Contains(guid)) unresolved.Add(guid);
            return;
        }

        // Recurse into any nested unmatched plan GUIDs first
        if (planJson["resource"]?["things"] is JsonArray planThings)
        {
            foreach (uint nested in CollectEmitterPlanGuids(planThings)
                         .Concat(CollectUntranslatedPlanGuids(planThings, options.Settings))
                         .Distinct()
                         .Where(g => g != guid && !converted.ContainsKey(g)))
            {
                ConvertPlan(nested, options, converted, generatedBins, unresolved, visited,
                    levelUnknownGuids, depth + 1);
            }

            // Translate internal asset GUIDs (materials, meshes, textures, etc.)
            var planUnknown = new List<uint>();
            LbpLevel.ReplaceGuids(planThings, options.Settings, planUnknown);
            foreach (uint u in planUnknown)
                if (!levelUnknownGuids.Contains(u)) levelUnknownGuids.Add(u);

            // Patch any nested plan references that were just converted
            if (converted.Count > 0)
                PatchPlanReferences(planThings, converted);
        }

        // Also translate asset refs inside inventoryData (e.g. icon texture)
        if (planJson["resource"]?["inventoryData"] is JsonNode invData)
        {
            var invUnknown = new List<uint>();
            LbpLevel.ReplaceGuids(invData, options.Settings, invUnknown);
            foreach (uint u in invUnknown)
                if (!levelUnknownGuids.Contains(u)) levelUnknownGuids.Add(u);
        }

        // Apply the minimal inventoryData transformation
        TransformInventoryData(planJson);

        // Serialize to binary and SHA1-hash
        byte[]? bytes = options.PlanSerializer(planJson);
        if (bytes == null)
        {
            if (!unresolved.Contains(guid)) unresolved.Add(guid);
            return;
        }

        string sha1 = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
        converted[guid]     = sha1;
        generatedBins[sha1] = bytes;
    }

    // ── inventoryData transformation ─────────────────────────────────────────

    /// <summary>
    /// Applies the minimal changes needed to make a built-in readymade plan
    /// look like a user-created object, based on the diff between the two
    /// sample files provided:
    ///
    ///   type:            ["READYMADE"]  → ["USER_OBJECT"]
    ///   titleKey:        (non-zero)     → 0
    ///   descriptionKey:  (non-zero)     → 0
    ///   colour:          (non-zero)     → 0
    ///   location:        (non-zero)     → 0
    ///   category:        (non-zero)     → 0
    ///   creationHistory: null           → []
    ///   flags:           0              → 1
    ///
    /// Everything else (creator, icon, photoData, etc.) is left exactly as-is.
    /// The icon texture GUID is already translated by ReplaceGuids above.
    /// </summary>
    private static void TransformInventoryData(JsonObject planJson)
    {
        if (planJson["resource"]?["inventoryData"] is not JsonObject inv)
            return;

        inv["type"]            = new JsonArray("USER_OBJECT");
        inv["titleKey"]        = 0;
        inv["descriptionKey"]  = 0;
        inv["colour"]          = 0;
        inv["location"]        = 0;
        inv["category"]        = 0u;
        inv["creationHistory"] = new JsonArray();
        inv["flags"]           = 1;
    }

    // ── Level / plan patching ─────────────────────────────────────────────────

    /// <summary>
    /// Walks <paramref name="node"/> replacing uint plan GUIDs in
    /// { "value": guid, "type": "PLAN" } descriptors with their SHA1 strings.
    /// planGUID on thing objects is an instance membership tag, not an asset
    /// reference, and must never be replaced with a hash.
    /// </summary>
    private static void PatchPlanReferences(
        JsonNode node, IReadOnlyDictionary<uint, string> guidToSha1)
    {
        if (node is JsonObject obj)
        {
            // Only { "value": <uint>, "type": "PLAN" } descriptors get the SHA1.
            // planGUID is an instance tag (which plan group a thing belongs to)
            // and stays as a uint forever.
            if (obj.ContainsKey("value") && obj.ContainsKey("type") &&
                obj["type"] is JsonValue tv && tv.TryGetValue(out string? typeName) &&
                typeName == "PLAN" &&
                TryGetUInt(obj["value"], out uint pv) &&
                guidToSha1.TryGetValue(pv, out string? pvSha1))
            {
                obj["value"] = pvSha1;
            }

            foreach ((string _, JsonNode? child) in obj.ToList())
                if (child != null) PatchPlanReferences(child, guidToSha1);
        }
        else if (node is JsonArray arr)
        {
            foreach (JsonNode? item in arr)
                if (item != null) PatchPlanReferences(item, guidToSha1);
        }
    }

    // ── GUID collection ───────────────────────────────────────────────────────

    /// <summary>Collects uint plan GUIDs from PEmitter.plan fields.</summary>
    private static IEnumerable<uint> CollectEmitterPlanGuids(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            if (obj["PEmitter"]?["plan"] is JsonObject planRef &&
                planRef["type"] is JsonValue pt &&
                pt.TryGetValue(out string? ptype) && ptype == "PLAN" &&
                TryGetUInt(planRef["value"], out uint g))
            {
                yield return g;
            }

            foreach ((string _, JsonNode? child) in obj)
                if (child != null)
                    foreach (uint r in CollectEmitterPlanGuids(child))
                        yield return r;
        }
        else if (node is JsonArray arr)
        {
            foreach (JsonNode? item in arr)
                if (item != null)
                    foreach (uint r in CollectEmitterPlanGuids(item))
                        yield return r;
        }
    }

    /// <summary>
    /// Collects planGUID / PLAN-typed value refs that have no match in
    /// the settings map (i.e. they are unmatched readymades that also need
    /// converting when found inside another plan's things).
    /// </summary>
    private static IEnumerable<uint> CollectUntranslatedPlanGuids(
        JsonNode node, ConversionSettings settings)
    {
        if (node is JsonObject obj)
        {
            if (obj.ContainsKey("planGUID") &&
                TryGetUInt(obj["planGUID"], out uint pg) &&
                NeedsConversion(pg, settings))
                yield return pg;

            if (obj.ContainsKey("value") && obj.ContainsKey("type") &&
                obj["type"] is JsonValue tv && tv.TryGetValue(out string? tn) &&
                tn == "PLAN" &&
                TryGetUInt(obj["value"], out uint pv) &&
                NeedsConversion(pv, settings))
                yield return pv;

            foreach ((string _, JsonNode? child) in obj)
                if (child != null)
                    foreach (uint r in CollectUntranslatedPlanGuids(child, settings))
                        yield return r;
        }
        else if (node is JsonArray arr)
        {
            foreach (JsonNode? item in arr)
                if (item != null)
                    foreach (uint r in CollectUntranslatedPlanGuids(item, settings))
                        yield return r;
        }
    }

    private static bool NeedsConversion(uint guid, ConversionSettings settings) =>
        !settings.AssetsMap.TryGetValue(guid, out LbpAsset? asset) || asset.ToKey == null;

    private static bool TryGetUInt(JsonNode? node, out uint value)
    {
        value = 0;
        if (node is not JsonValue jv) return false;
        if (jv.TryGetValue(out uint u))           { value = u;       return true; }
        if (jv.TryGetValue(out int  i) && i >= 0) { value = (uint)i; return true; }
        if (jv.TryGetValue(out long l) && l is >= 0 and <= uint.MaxValue)
                                                   { value = (uint)l; return true; }
        return false;
    }
}