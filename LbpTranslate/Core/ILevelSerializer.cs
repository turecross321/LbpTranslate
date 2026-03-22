namespace LbpTranslate.Core;

public interface ILevelSerializer
{
    /// <summary>Deserializes a level file into an <see cref="LbpLevel"/>.</summary>
    LbpLevel Deserialize(string path);
 
    /// <summary>Serializes an <see cref="LbpLevel"/> to the given output path.</summary>
    void Serialize(LbpLevel level, string outputPath);
}
