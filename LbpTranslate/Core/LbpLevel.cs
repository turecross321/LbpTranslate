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

    private void ReplaceGuids(JsonNode node, ConversionSettings settings)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                foreach ((string key, JsonNode? value) in obj.ToList())
                {
                    if (key == "planGUID")
                    {
                        uint? oldGuid = obj[key]?.GetValue<uint?>();
                        uint? newGuid;
                        
                        if (oldGuid == null)
                            continue;

                        settings.GuidMap.TryGetValue((uint)oldGuid, out LbpAsset? asset);
                        
                        if (asset?.ToGuid == null)
                        {
                            if (asset?.Category != null)
                            {
                                settings.CategoryDefaults.TryGetValue(asset.Category, out uint defaultGuid);
                                newGuid = defaultGuid;
                                Console.WriteLine($"Defaulting asset [{asset.Category}] to {defaultGuid}");
                            }
                            else
                            {
                                if (!_unknownGuids.Contains((uint)oldGuid))
                                    _unknownGuids.Add((uint)oldGuid);
                            
                                Console.WriteLine($"Unknown asset not part of settings {settings.DefaultGuid}");
                                newGuid = settings.DefaultGuid;
                            }
                        }
                        else
                            newGuid = asset.ToGuid;
                        
                        obj[key] = newGuid;
                    }
                    else if (value != null)
                    {
                        ReplaceGuids(value, settings);
                    }
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
}