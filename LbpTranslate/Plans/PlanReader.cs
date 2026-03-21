using System.Text;

namespace LbpTranslate.Plans;

[Flags]
public enum InventoryObjectType : uint
{
    None              = 0,
    PrimitiveMaterial = 1 << 0,
    Readymade         = 1 << 1,
    Decoration        = 1 << 2,
    Sticker           = 1 << 3,
    Costume           = 1 << 4,
    CostumeMaterial   = 1 << 5,
    Joint             = 1 << 6,
    UserObject        = 1 << 7,
    Background        = 1 << 8,
    GameplayKit       = 1 << 9,
    UserSticker       = 1 << 10,
    PrimitiveShape    = 1 << 11,
    Sequencer         = 1 << 12,
    Danger            = 1 << 13,
    Eyetoy            = 1 << 14,
    Gadget            = 1 << 15,
    Tool              = 1 << 16,
    SackbotMesh       = 1 << 17,
    Music             = 1 << 21,
    Sound             = 1 << 22,
    Instrument        = 1 << 29,
}

public static class PlanReader
{
    private const int RevDependencies = 0x109;
    private const int RevHeaderFlags  = 0x189;
    private const int RevBranch       = 0x271;
    private const int RevCompression  = 0x297;
    private const int RevPlanDetails  = 0x197;
    private const int RevMiddlePath   = 0x233;
    private const int RevNewInventory = 0x37C;

    // isUsedForStreaming is written only when subVersion (head >> 16) >= 0xCC
    // This is an LBP3-only field; LBP1/2/Vita plans never have it.
    private const int RevStreamingPlanSubVersion = 0xCC;

    private const short BranchLeerdammer = 0x4c44;
    private const short LdResources      = 0x2;
    private const byte  UseCompressedIntegers = 1;

    public static InventoryObjectType ReadType(string path)
    {
        try   { return ParsePlan(File.ReadAllBytes(path)); }
        catch { return InventoryObjectType.None; }
    }

    private static InventoryObjectType ParsePlan(byte[] data)
    {
        if (data.Length < 8) return InventoryObjectType.None;
        if (Encoding.ASCII.GetString(data, 0, 3) != "PLN") return InventoryObjectType.None;
        if ((char)data[3] != 'b') return InventoryObjectType.None;

        int pos = 4;
        int head = ReadInt32BE(data, pos); pos += 4;

        // subVersion lives in the upper 16 bits of head (LBP3 only)
        int version    = head & 0xFFFF;
        int subVersion = (head >> 16) & 0xFFFF;

        int dependencyTableOffset = -1;
        if (version >= RevDependencies)
            dependencyTableOffset = ReadInt32BE(data, pos); pos += 4;

        short branchID = 0, branchRevision = 0;
        byte compressionFlags = 0;
        bool isCompressed = version < RevHeaderFlags;

        if (version >= RevHeaderFlags)
        {
            if (version >= RevBranch)
            {
                branchID       = ReadInt16BE(data, pos); pos += 2;
                branchRevision = ReadInt16BE(data, pos); pos += 2;
            }

            bool isLeerdammer = branchID == BranchLeerdammer && branchRevision >= LdResources;
            if (version >= RevCompression || isLeerdammer)
                compressionFlags = data[pos++];

            isCompressed = data[pos++] != 0;
        }

        byte[] planData = isCompressed
            ? DecompressChunked(data, pos, dependencyTableOffset)
            : SliceToEnd(data, pos, dependencyTableOffset);

        if (planData == null) return InventoryObjectType.None;

        return ParseRPlan(planData, version, subVersion, compressionFlags);
    }

    private static InventoryObjectType ParseRPlan(
        byte[] data, int version, int subVersion, byte compressionFlags)
    {
        int pos = 0;

        // isUsedForStreaming only present in LBP3 (subVersion >= 0xCC)
        if (subVersion >= RevStreamingPlanSubVersion)
        {
            bool isUsedForStreaming = data[pos++] != 0;
            if (isUsedForStreaming) return InventoryObjectType.None;
        }

        // i32 plan revision (ULEB128 when compressed)
        SkipInt32(data, ref pos, compressionFlags);

        // byte[] thingData (length-prefixed)
        int thingDataLen = ReadInt32(data, ref pos, compressionFlags);
        if (pos + thingDataLen > data.Length) return InventoryObjectType.None;
        pos += thingDataLen;

        if (version < RevPlanDetails) return InventoryObjectType.None;
        if (pos >= data.Length)       return InventoryObjectType.None;

        return ParseInventoryItemDetails(data, pos, version, compressionFlags);
    }

    private static InventoryObjectType ParseInventoryItemDetails(
        byte[] data, int pos, int version, byte compressionFlags)
    {
        // ── Path A: version > 0x37C ──────────────────────────────────────────
        if (version > RevNewInventory)
        {
            SkipInt64(data, ref pos, compressionFlags);  // s64 dateAdded
            SkipInt32(data, ref pos, compressionFlags);  // enum32 slotType
            SkipInt32(data, ref pos, compressionFlags);  // u32 slotNumber
            SkipInt32(data, ref pos, compressionFlags);  // guid highlightSound
            SkipInt32(data, ref pos, compressionFlags);  // i32 colour
            if (pos >= data.Length) return InventoryObjectType.None;
            return (InventoryObjectType)(uint)ReadInt32(data, ref pos, compressionFlags);
        }

        // ── Path B: 0x233 <= version <= 0x37C  (all force32=true, LE) ────────
        if (version >= RevMiddlePath)
        {
            pos += 4;  // guid highlightSound
            pos += 4;  // enum32 slotType
            pos += 4;  // u32 slotNumber
            pos += 4;  // i32 locationIndex
            pos += 4;  // i32 categoryIndex
            pos += 4;  // i32 primaryIndex
            pos += 4;  // i32 lastUsed
            pos += 4;  // i32 numUses
            if (version > 0x234) pos += 4; // i32 pad
            pos += 8;  // s64 dateAdded
            pos += 4;  // i32 fluffCost
            pos += 4;  // i32 colour
            if (pos + 4 > data.Length) return InventoryObjectType.None;
            return (InventoryObjectType)(uint)ReadInt32LE(data, pos);
        }

        // ── Path C: < 0x233 ──────────────────────────────────────────────────
        return InventoryObjectType.None;
    }

    // ── Decompression ────────────────────────────────────────────────────────

    private static byte[] DecompressChunked(byte[] data, int offset, int endOffset)
    {
        if (offset + 4 > data.Length) return null!;

        int chunkCount = ReadUInt16BE(data, offset + 2);
        offset += 4;

        if (chunkCount == 0)
            return SliceToEnd(data, offset, endOffset);

        int[] compSizes   = new int[chunkCount];
        int[] decompSizes = new int[chunkCount];
        int   total       = 0;

        for (int i = 0; i < chunkCount; i++)
        {
            compSizes[i]   = ReadUInt16BE(data, offset); offset += 2;
            decompSizes[i] = ReadUInt16BE(data, offset); offset += 2;
            total         += decompSizes[i];
        }

        using var output = new MemoryStream(total);
        for (int i = 0; i < chunkCount; i++)
        {
            if (compSizes[i] == decompSizes[i])
            {
                output.Write(data, offset, compSizes[i]);
            }
            else
            {
                // Full zlib stream (keep the 2-byte header)
                using var input   = new MemoryStream(data, offset, compSizes[i]);
                using var deflate = new System.IO.Compression.ZLibStream(
                    input, System.IO.Compression.CompressionMode.Decompress);
                deflate.CopyTo(output);
            }
            offset += compSizes[i];
        }

        return output.ToArray();
    }

    private static byte[] SliceToEnd(byte[] data, int offset, int endOffset)
    {
        int end = endOffset > 0 ? Math.Min(endOffset, data.Length) : data.Length;
        if (offset >= end) return null!;
        return data[offset..end];
    }

    // ── Integer readers ──────────────────────────────────────────────────────

    private static int ReadInt32(byte[] data, ref int pos, byte compressionFlags)
    {
        if ((compressionFlags & UseCompressedIntegers) == 0)
        { int v = ReadInt32BE(data, pos); pos += 4; return v; }
        return (int)(ReadULEB128(data, ref pos) & 0xFFFF_FFFF);
    }

    private static void SkipInt32(byte[] data, ref int pos, byte compressionFlags)
        => ReadInt32(data, ref pos, compressionFlags);

    private static void SkipInt64(byte[] data, ref int pos, byte compressionFlags)
    {
        if ((compressionFlags & UseCompressedIntegers) == 0) { pos += 8; return; }
        ReadULEB128(data, ref pos);
    }

    private static ulong ReadULEB128(byte[] data, ref int pos)
    {
        ulong result = 0; int shift = 0;
        while (pos < data.Length)
        {
            byte b = data[pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        return result;
    }

    private static int   ReadInt32BE(byte[] data, int pos) =>
        (data[pos] << 24) | (data[pos+1] << 16) | (data[pos+2] << 8) | data[pos+3];
    private static short ReadInt16BE(byte[] data, int pos) =>
        (short)((data[pos] << 8) | data[pos+1]);
    private static int   ReadUInt16BE(byte[] data, int pos) =>
        (data[pos] << 8) | data[pos+1];
    private static int   ReadInt32LE(byte[] data, int pos) =>
        data[pos] | (data[pos+1] << 8) | (data[pos+2] << 16) | (data[pos+3] << 24);
}