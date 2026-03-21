using System.Buffers.Binary;

namespace LbpTranslate.BluRayGuids;

public class FileDb
{
    public int Revision { get; private set; }
    public List<FileDbEntry> Entries { get; } = new();
    public Dictionary<uint, FileDbEntry> Lookup { get; } = new();

    private static string GetFolderFromExtension(string extension)
    {
        return extension.ToLower() switch
        {
            ".slt" => "slots/",
            ".tex" => "textures/",
            ".bpr" or ".ipr" => "profiles/",
            ".mol" or ".msh" => "models/",
            ".gmat" or ".gmt" => "gfx/",
            ".mat" => "materials/",
            ".ff" or ".fsh" => "scripts/",
            ".plan" or ".pln" => "plans/",
            ".pal" => "palettes/",
            ".oft" => "outfits/",
            ".sph" => "skeletons/",
            ".bin" or ".lvl" => "levels/",
            ".vpo" => "shaders/vertex/",
            ".fpo" => "shaders/fragment/",
            ".anim" or ".anm" => "animations/",
            ".bev" => "bevels/",
            ".smh" => "static_meshes/",
            ".mus" => "audio/settings/",
            ".fsb" => "audio/music/",
            ".txt" => "text/",
            _ => "unknown/"
        };
    }
    
    public static FileDb Load(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);

        var db = new FileDb();

        db.Revision = BinaryPrimitives.ReadInt32BigEndian(br.ReadBytes(4));
        int count = BinaryPrimitives.ReadInt32BigEndian(br.ReadBytes(4));

        bool isLbp3 = (db.Revision >> 16) >= 0x148;

        for (int i = 0; i < count; i++)
        {
            string pathStr;
            if (isLbp3)
            {
                // Java reads i16 for length, then reads that many chars (not null-terminated)
                short pathLen = BinaryPrimitives.ReadInt16BigEndian(br.ReadBytes(2));
                pathStr = System.Text.Encoding.UTF8.GetString(br.ReadBytes(pathLen));
            }
            else
            {
                // Java reads i32 for length, then reads that many chars
                int pathLen = BinaryPrimitives.ReadInt32BigEndian(br.ReadBytes(4));
                pathStr = System.Text.Encoding.UTF8.GetString(br.ReadBytes(pathLen));
            }

            long timestamp = isLbp3
                ? BinaryPrimitives.ReadUInt32BigEndian(br.ReadBytes(4))  // u32
                : BinaryPrimitives.ReadInt64BigEndian(br.ReadBytes(8));  // s64

            uint size = BinaryPrimitives.ReadUInt32BigEndian(br.ReadBytes(4));
            byte[] sha1 = br.ReadBytes(20); // SHA1 is just raw bytes, endianness doesn't apply
            uint guid = BinaryPrimitives.ReadUInt32BigEndian(br.ReadBytes(4));

            // handle .ext-only paths like the Java code does
            if (pathStr.StartsWith("."))
            {
                string sha1Hex = Convert.ToHexString(sha1).ToLower();
                string folder = GetFolderFromExtension(pathStr);
                pathStr = $"data/{folder}{sha1Hex}{pathStr}";
            }

            var entry = new FileDbEntry
            {
                Path = pathStr,
                Timestamp = timestamp,
                Size = size,
                Sha1 = sha1,
                Guid = guid
            };

            if (!db.Lookup.ContainsKey(guid))
            {
                db.Entries.Add(entry);
                db.Lookup[guid] = entry;
            }
        }

        return db;
    }
}