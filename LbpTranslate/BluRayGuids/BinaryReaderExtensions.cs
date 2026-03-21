namespace LbpTranslate.BluRayGuids;

public static class BinaryReaderExtensions
{
    public static string ReadNullTerminatedString(this BinaryReader br)
    {
        var bytes = new List<byte>();
        byte b;
        while ((b = br.ReadByte()) != 0)
            bytes.Add(b);
        return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
    }

    public static string ReadFixedString(this BinaryReader br, int length)
    {
        var bytes = br.ReadBytes(length);
        int nullIndex = Array.IndexOf(bytes, (byte)0);
        if (nullIndex >= 0)
            bytes = bytes.Take(nullIndex).ToArray();
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}