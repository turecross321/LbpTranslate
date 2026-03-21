namespace LbpTranslate.BluRayGuids;

public class FileDbEntry
{
    public string Path { get; set; }
    public long Timestamp { get; set; }
    public uint Size { get; set; }
    public byte[] Sha1 { get; set; }

    public uint Guid { get; set; }
}