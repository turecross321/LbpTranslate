using System.Text.Json;
using System.Text.Json.Nodes;

namespace LbpTranslate.Core;

public class LbpLevel
{
    private readonly JsonNode _root;
    private readonly List<uint> _unknownGuids = [];
    public List<uint> GetUnknownGuids() => _unknownGuids;
    
    public LbpLevel(string inputPath)
    {
        string json = File.ReadAllText(inputPath);
        this._root = JsonNode.Parse(json)!;
    }

    public void SetRevision(int revision)
    {
        _root["revision"] = revision;
    }

    public void Export(string output)
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = true
        };

        File.WriteAllText(output, _root.ToJsonString(options));
    }
    
    public void TranslateGuids(ConversionSettings settings)
    {
        ReplaceGuids(_root, settings);
    }

    private static readonly HashSet<string> PlanGuidTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "PLAN",
    };

    private static bool IsPlanGuidObject(JsonObject obj, out uint guid)
    {
        guid = 0;
        JsonNode? typeNode  = obj["type"];
        JsonNode? valueNode = obj["value"];
        if (typeNode == null || valueNode == null) return false;
        if (!PlanGuidTypes.Contains(typeNode.GetValue<string>())) return false;
        try { guid = valueNode.GetValue<uint>(); return true; }
        catch { return false; }
    }

    private uint? ResolveGuid(uint oldGuid, ConversionSettings settings)
    {
        if (!settings.GuidMap.TryGetValue(oldGuid, out LbpAsset? asset))
        {
            if (!_unknownGuids.Contains(oldGuid))
                _unknownGuids.Add(oldGuid);
            Console.WriteLine($"No mapping found for guid {oldGuid}, defaulting to {settings.DefaultGuid}");
            return settings.DefaultGuid;
        }

        if (asset.ToGuid == null &&
            settings.CategoryDefaults.TryGetValue(asset.Category, out uint categoryDefault))
            return categoryDefault;

        return asset.ToGuid;
    }

    private void ReplaceGuids(JsonNode node, ConversionSettings settings)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                if (IsPlanGuidObject(obj, out uint oldGuid))
                    obj["value"] = ResolveGuid(oldGuid, settings);

                foreach ((string key, JsonNode? value) in obj.ToList())
                {
                    if (key == "planGUID" && obj[key]?.GetValue<uint?>() is { } planGuid)
                        obj[key] = ResolveGuid(planGuid, settings);
                    else if (value != null)
                        ReplaceGuids(value, settings);
                }

                break;
            }
            case JsonArray arr:
            {
                foreach (JsonNode? item in arr)
                    if (item != null) ReplaceGuids(item, settings);
                break;
            }
        }
    }
}