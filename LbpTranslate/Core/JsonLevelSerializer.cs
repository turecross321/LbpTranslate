namespace LbpTranslate.Core;

/// <summary>
/// Reads and writes levels that are already in the toolkit JSON format.
/// </summary>
public class JsonLevelSerializer : ILevelSerializer
{
    public LbpLevel Deserialize(string path) => new LbpLevel(path);
 
    public void Serialize(LbpLevel level, string outputPath) => level.Export(outputPath);
}
