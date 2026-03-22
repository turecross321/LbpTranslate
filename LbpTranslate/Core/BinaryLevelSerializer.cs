namespace LbpTranslate.Core;

/// <summary>
/// Reads and writes binary LBP level files by delegating to the toolkit's
/// jsoninator JAR for the binary &lt;-&gt; JSON conversion step.
/// The JSON is written to a temp file that is cleaned up automatically.
/// </summary>
public class BinaryLevelSerializer : ILevelSerializer
{
    private readonly Jsoninator _jsoninator;

    /// <param name="jarPath">Absolute or relative path to jsoninator.jar.</param>
    /// <param name="log">Optional log delegate forwarded to <see cref="Jsoninator"/>.</param>
    /// <param name="maxHeapMb">JVM heap cap in MB forwarded to <see cref="Jsoninator"/>.</param>
    public BinaryLevelSerializer(string jarPath, Action<string>? log = null, int maxHeapMb = 256)
    {
        _jsoninator = new Jsoninator(jarPath, log) { MaxHeapMb = maxHeapMb };
    }

    public LbpLevel Deserialize(string path)
    {
        string tempJson = Path.ChangeExtension(Path.GetTempFileName(), ".json");
        try
        {
            _jsoninator.Convert(path, tempJson);
            return new LbpLevel(tempJson);
        }
        finally
        {
            if (File.Exists(tempJson)) File.Delete(tempJson);
        }
    }

    public void Serialize(LbpLevel level, string outputPath)
    {
        string tempJson = Path.ChangeExtension(Path.GetTempFileName(), ".json");
        try
        {
            level.Export(tempJson);
            _jsoninator.Convert(tempJson, outputPath);
        }
        finally
        {
            if (File.Exists(tempJson)) File.Delete(tempJson);
        }
    }
}