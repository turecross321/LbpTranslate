using System.Diagnostics;

namespace LbpTranslate.Core;

 
/// <summary>
/// Reads and writes binary LBP level files by delegating to the toolkit's
/// jsoninator JAR for the binary &lt;-&gt; JSON conversion step.
/// The JSON is written to a temp file that is cleaned up automatically.
/// </summary>
public class BinaryLevelSerializer : ILevelSerializer
{
    private readonly string _jarPath;
 
    /// <param name="jarPath">Absolute or relative path to jsoninator.jar.</param>
    public BinaryLevelSerializer(string jarPath)
    {
        _jarPath = jarPath;
    }
 
    public LbpLevel Deserialize(string path)
    {
        string tempJson = Path.ChangeExtension(Path.GetTempFileName(), ".json");
        try
        {
            RunJsoninator(path, tempJson);
            return new LbpLevel(tempJson);
        }
        finally
        {
            if (File.Exists(tempJson))
                File.Delete(tempJson);
        }
    }
 
    public void Serialize(LbpLevel level, string outputPath)
    {
        string tempJson = Path.ChangeExtension(Path.GetTempFileName(), ".json");
        try
        {
            level.Export(tempJson);
            RunJsoninator(tempJson, outputPath);
        }
        finally
        {
            if (File.Exists(tempJson))
                File.Delete(tempJson);
        }
    }
 
    private void RunJsoninator(string input, string output)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = "java",
            Arguments              = $"-jar \"{_jarPath}\" \"{input}\" \"{output}\"",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        };
 
        using Process process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start jsoninator process.");
 
        process.WaitForExit();
 
        if (process.ExitCode != 0)
        {
            string error = process.StandardError.ReadToEnd().Trim();
            throw new InvalidOperationException(
                $"jsoninator exited with code {process.ExitCode}: {error}");
        }
    }
}