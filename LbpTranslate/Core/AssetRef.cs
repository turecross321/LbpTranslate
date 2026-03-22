using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LbpTranslate.Core;

/// <summary>
/// Represents an asset reference that is either a numeric GUID or a SHA1 hash.
///
/// In level/plan JSON (read by the game engine):
///   - GUID  → raw uint number:       56300
///   - SHA1  → 40-char hex string:    "8ddb4841..."
///
/// In settings JSON and CLI input (tool-only):
///   - GUID  → "g&lt;uint&gt;" string: "g56300"
///   - SHA1  → same 40-char hex:      "8ddb4841..."
/// </summary>
public readonly struct AssetRef
{
    public readonly uint?   Guid;
    public readonly string? Sha1;

    public bool IsGuid => Guid.HasValue;
    public bool IsSha1 => Sha1 != null;

    private AssetRef(uint guid)   { Guid = guid; Sha1 = null; }
    private AssetRef(string sha1) { Guid = null; Sha1 = sha1; }

    public static AssetRef FromGuid(uint guid)   => new(guid);
    public static AssetRef FromSha1(string sha1) => new(sha1);

    /// <summary>
    /// Returns the string representation for settings JSON / CLI display:
    /// "g56300" for GUIDs, plain hex for SHA1s.
    /// </summary>
    public override string ToString() => IsGuid ? $"g{Guid!.Value}" : Sha1!;

    /// <summary>
    /// Returns the JsonNode to write into a level or plan JSON document
    /// (what the game engine reads): raw uint for GUIDs, plain hex string for SHA1s.
    /// </summary>
    public JsonNode ToJsonNode() =>
        Guid.HasValue
            ? JsonValue.Create(Guid.Value)!
            : JsonValue.Create(Sha1!)!;

    // ── Parsing from JSON nodes (level/plan + settings) ───────────────────────

    /// <summary>
    /// Tries to parse from a JsonNode. Accepts:
    ///   - Raw integer (uint/int/long)    — GUID in level/plan JSON
    ///   - "g&lt;uint&gt;" string          — GUID in settings JSON
    ///   - 40-char hex string             — SHA1 in either context
    /// </summary>
    public static bool TryParse(JsonNode? node, out AssetRef result)
    {
        result = default;
        if (node is not JsonValue jv) return false;

        if (jv.TryGetValue(out string? s) && s != null)
            return TryParseString(s, out result);

        if (jv.TryGetValue(out uint u))              { result = FromGuid(u);       return true; }
        if (jv.TryGetValue(out int i)  && i >= 0)    { result = FromGuid((uint)i); return true; }
        if (jv.TryGetValue(out long l) && l is >= 0 and <= uint.MaxValue)
                                                     { result = FromGuid((uint)l); return true; }
        return false;
    }

    /// <summary>
    /// Tries to parse from a plain string (CLI input or settings JSON value).
    /// Accepts "g&lt;uint&gt;" for GUIDs and 40-char hex for SHA1s.
    /// Does NOT accept bare integers — the user must type "g56300", not "56300".
    /// </summary>
    public static bool TryParseSettingsString(string? s, out AssetRef result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        return TryParseString(s, out result);
    }

    private static bool TryParseString(string s, out AssetRef result)
    {
        result = default;
        if (s.Length > 1 && s[0] == 'g' && uint.TryParse(s.AsSpan(1), out uint g))
        { result = FromGuid(g); return true; }
        if (s.Length == 40 && s.All(Uri.IsHexDigit))
        { result = FromSha1(s); return true; }
        return false;
    }
}

/// <summary>
/// JsonConverter for AssetRef fields in settings JSON.
/// Writes "g&lt;uint&gt;" for GUIDs, plain hex for SHA1s; reads both forms plus
/// bare integers (backwards compatibility with old settings files).
/// When placed on a nullable AssetRef? field, STJ handles the null token automatically.
/// </summary>
public sealed class AssetKeyJsonConverter : JsonConverter<AssetRef>
{
    public static readonly AssetKeyJsonConverter Instance = new();

    public override AssetRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            string s = reader.GetString()!;
            if (AssetRef.TryParseSettingsString(s, out AssetRef r)) return r;
            throw new JsonException($"Cannot parse asset key: \"{s}\"");
        }

        // backwards compat: bare integer in old settings files
        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetUInt32(out uint u)) return AssetRef.FromGuid(u);
            if (reader.TryGetInt64(out long l) && l is >= 0 and <= uint.MaxValue)
                return AssetRef.FromGuid((uint)l);
        }

        throw new JsonException($"Cannot read asset key from token {reader.TokenType}");
    }

    public override void Write(Utf8JsonWriter writer, AssetRef value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString()); // "g56300" or sha1
    }
}