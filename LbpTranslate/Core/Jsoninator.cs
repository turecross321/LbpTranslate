using System.Diagnostics;
using System.Text.Json.Nodes;

namespace LbpTranslate.Core;

/// <summary>
/// Thin wrapper around the jsoninator JAR that handles process invocation,
/// error propagation, and temp-file cleanup.
/// </summary>
public class Jsoninator
{
    private readonly string         _jarPath;
    private readonly Action<string> _log;

    /// <summary>
    /// Maximum JVM heap size in megabytes passed as <c>-Xmx</c>.
    /// Defaults to 256 MB, which is sufficient for typical LBP level and plan files
    /// Increase if jsoninator throws OutOfMemoryError on very large files.
    /// </summary>
    public int MaxHeapMb { get; init; } = 256;

    /// <param name="jarPath">Absolute or relative path to jsoninator.jar.</param>
    /// <param name="log">
    /// Optional sink for progress messages.  Each invocation logs one line before
    /// it starts and one line after it finishes (or fails).  Defaults to no-op.
    /// </param>
    public Jsoninator(string jarPath, Action<string>? log = null)
    {
        if (!File.Exists(jarPath))
            throw new FileNotFoundException($"jsoninator.jar not found at: {jarPath}", jarPath);
        _jarPath = jarPath;
        _log     = log ?? (_ => { });
    }

    /// <summary>
    /// Converts <paramref name="inputPath"/> to <paramref name="outputPath"/>
    /// (binary → JSON or JSON → binary depending on file contents).
    /// Throws <see cref="InvalidOperationException"/> if jsoninator exits non-zero.
    /// </summary>
    public void Convert(string inputPath, string outputPath)
    {
        _log($"  [jsoninator] {inputPath} -> {outputPath}");

        var psi = new ProcessStartInfo
        {
            FileName               = "java",
            Arguments              = $"-Xmx{MaxHeapMb}m -jar \"{_jarPath}\" \"{inputPath}\" \"{outputPath}\"",
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
            _log($"  [jsoninator] failed (exit {process.ExitCode}): {error}");
            throw new InvalidOperationException(
                $"jsoninator exited with code {process.ExitCode}: {error}");
        }

        _log($"  [jsoninator] ok");
    }

    /// <summary>
    /// Converts a binary plan file to a <see cref="JsonObject"/> without leaving
    /// any files on disk.  Returns null if conversion fails for any reason.
    /// </summary>
    public JsonObject? ConvertPlanToJson(string binaryPlanPath)
    {
        string tempJson = Path.ChangeExtension(Path.GetTempFileName(), ".json");
        try
        {
            Convert(binaryPlanPath, tempJson);
            string text = File.ReadAllText(tempJson);
            return JsonNode.Parse(text, null,
                new System.Text.Json.JsonDocumentOptions { MaxDepth = 256 }) as JsonObject;
        }
        catch { return null; }
        finally
        {
            if (File.Exists(tempJson)) File.Delete(tempJson);
        }
    }

    /// <summary>
    /// Returns true if a <c>jsoninator.jar</c> file exists next to the running
    /// executable, and sets <paramref name="jarPath"/> to its full path.
    /// </summary>
    public static bool TryFindJar(out string jarPath)
    {
        jarPath = Path.Combine(AppContext.BaseDirectory, "jsoninator.jar");
        return File.Exists(jarPath);
    }
}