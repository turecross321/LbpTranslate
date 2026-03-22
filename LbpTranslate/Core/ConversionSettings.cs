using System.Text.Json;
using System.Text.Json.Serialization;
using LbpTranslate.Plans;

namespace LbpTranslate.Core;

public class ConversionSettings
{
    public required string FromGame;
    public required string ToGame;

    /// <summary>
    /// Maps source-game uint GUIDs to their asset entries.
    /// Keys are always uint GUIDs (from the bluray map); serialised as "g&lt;uint&gt;" strings.
    /// </summary>
    [JsonConverter(typeof(GuidMapConverter))]
    public required Dictionary<uint, LbpAsset> AssetsMap;

    /// <summary>
    /// Global fallback asset key when no mapping is found and no category default applies.
    /// Can be a uint GUID ("g56300") or a SHA1 hash.
    /// </summary>
    [JsonConverter(typeof(AssetKeyJsonConverter))]
    public required AssetRef? DefaultKey;

    /// <summary>
    /// Per-category fallback asset keys.
    /// Values can be uint GUIDs ("g56300") or SHA1 hashes.
    /// </summary>
    [JsonConverter(typeof(CategoryDefaultsConverter))]
    public Dictionary<AssetCategory, AssetRef> CategoryDefaults = new();

    // ── Backwards-compat accessors ─────────────────────────────────────────

    public uint? DefaultGuid => DefaultKey?.IsGuid == true ? DefaultKey.Value.Guid : null;
}

/// <summary>
/// Reads/writes Dictionary&lt;uint, LbpAsset&gt; with "g&lt;uint&gt;" string keys.
/// </summary>
public sealed class GuidMapConverter : System.Text.Json.Serialization.JsonConverter<Dictionary<uint, LbpAsset>>
{
    public override Dictionary<uint, LbpAsset> Read(
        ref Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        var dict = new Dictionary<uint, LbpAsset>();
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new System.Text.Json.JsonException("Expected object");
        reader.Read();
        while (reader.TokenType != JsonTokenType.EndObject)
        {
            string keyStr = reader.GetString()!;
            uint key;
            if (keyStr.Length > 1 && keyStr[0] == 'g' && uint.TryParse(keyStr.AsSpan(1), out uint g))
                key = g;
            else if (uint.TryParse(keyStr, out uint u))
                key = u; // backwards compat
            else
                throw new System.Text.Json.JsonException($"Cannot parse GuidMap key: {keyStr}");

            reader.Read();
            var asset = System.Text.Json.JsonSerializer.Deserialize<LbpAsset>(ref reader, options)
                        ?? throw new System.Text.Json.JsonException("Null LbpAsset");
            dict[key] = asset;
            reader.Read();
        }
        return dict;
    }

    public override void Write(
        Utf8JsonWriter writer, Dictionary<uint, LbpAsset> value, System.Text.Json.JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (k, v) in value)
        {
            writer.WritePropertyName($"g{k}");
            System.Text.Json.JsonSerializer.Serialize(writer, v, options);
        }
        writer.WriteEndObject();
    }
}

/// <summary>
/// Reads/writes Dictionary&lt;AssetCategory, AssetRef&gt; with string category keys
/// and "g&lt;uint&gt;" or SHA1 string values.
/// </summary>
public sealed class CategoryDefaultsConverter : System.Text.Json.Serialization.JsonConverter<Dictionary<AssetCategory, AssetRef>>
{
    public override Dictionary<AssetCategory, AssetRef> Read(
        ref Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        var dict = new Dictionary<AssetCategory, AssetRef>();
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new System.Text.Json.JsonException("Expected object");
        reader.Read();
        while (reader.TokenType != JsonTokenType.EndObject)
        {
            string catStr = reader.GetString()!;
            if (!Enum.TryParse<AssetCategory>(catStr, out var cat))
                throw new System.Text.Json.JsonException($"Unknown category: {catStr}");
            reader.Read();

            var keyConverter = AssetKeyJsonConverter.Instance;
            AssetRef? v = keyConverter.Read(ref reader, typeof(AssetRef?), options);
            if (v is null) throw new System.Text.Json.JsonException("Null CategoryDefault value");
            dict[cat] = v.Value;
            reader.Read();
        }
        return dict;
    }

    public override void Write(
        Utf8JsonWriter writer, Dictionary<AssetCategory, AssetRef> value, System.Text.Json.JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (cat, key) in value)
        {
            writer.WritePropertyName(cat.ToString());
            AssetKeyJsonConverter.Instance.Write(writer, key, options);
        }
        writer.WriteEndObject();
    }
}