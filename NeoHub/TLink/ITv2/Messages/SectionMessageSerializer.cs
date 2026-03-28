using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages;

/// <summary>
/// Holds all shared fields for section messages (read, write, response).
/// Used compositionally to ensure correct wire order and avoid inheritance issues.
/// </summary>
internal class SectionMessageSerializer
{
    public byte[] MessageBytes
    {
        get => Serialize();
        set => Deserialize(value);
    }

    private MessageEncodingFlags _encoding;
    public int? Module { get; set; }
    public ushort[] SectionAddress { get; set; } = Array.Empty<ushort>();
    public byte Index { get; set; } = 0;
    public byte Count { get; set; } = 1;
    public byte[] SectionData { get; set; } = Array.Empty<byte>();
    private void Deserialize(byte[] messageBytes)
    {
        int encodedLength = 1;
        if (messageBytes.Length < encodedLength) throw new ArgumentException("Message bytes too short to decode", nameof(messageBytes));

        int index = 0;

        _encoding = (MessageEncodingFlags)messageBytes[index++];

        if (_encoding.HasFlag(MessageEncodingFlags.ModuleNumberIsUsed))
            encodedLength++;

        int sectionAddressLength = 1;
        sectionAddressLength += ((byte)_encoding >> 4) & 0x07;

        encodedLength += sectionAddressLength * 2;

        if (_encoding.HasFlag(MessageEncodingFlags.IndexIsUsed))
            encodedLength++;
        if (_encoding.HasFlag(MessageEncodingFlags.CountIsUsed))
            encodedLength++;

        if (messageBytes.Length < encodedLength) throw new ArgumentException("Message bytes too short to decode", nameof(messageBytes));

        if (_encoding.HasFlag(MessageEncodingFlags.ModuleNumberIsUsed))
        {
            Module = messageBytes[index++];
        }

        SectionAddress = new ushort[sectionAddressLength];

        for (int i = 0; i < sectionAddressLength; i++)
        {
            SectionAddress[i] = (ushort)(messageBytes[index++] << 8 | messageBytes[index++]);
        }

        if (_encoding.HasFlag(MessageEncodingFlags.IndexIsUsed))
        {
            Index = messageBytes[index++];
        }

        if (_encoding.HasFlag(MessageEncodingFlags.CountIsUsed))
        {
            Count = messageBytes[index++];
        }

        SectionData = messageBytes.Skip(index).ToArray();
    }
    private byte[] Serialize()
    {
        bool useIndex = Index > 0;

        bool useCount = Count > 1;

        List<byte> bytes = new List<byte>();
        MessageEncodingFlags encoding = 0;
        if (Module.HasValue)
            encoding |= MessageEncodingFlags.ModuleNumberIsUsed;
        if (useIndex)
            encoding |= MessageEncodingFlags.IndexIsUsed;
        if (useCount)
            encoding |= MessageEncodingFlags.CountIsUsed;
        if (SectionAddress.Length < 1)
            throw new InvalidOperationException("At least one section address part is required");
        if (SectionAddress.Length > 8)
            throw new InvalidOperationException("Cannot encode more than 8 section address words");

        encoding |= (MessageEncodingFlags)((SectionAddress.Length - 1) << 4);

        bytes.Add((byte)encoding);

        if (Module.HasValue)
            bytes.Add((byte)Module.Value);

        foreach (var addressWord in SectionAddress)
        {
            bytes.Add((byte)(addressWord >> 8));
            bytes.Add((byte)(addressWord & 0xFF));
        }
        if (useIndex)
            bytes.Add(Index);
        if (useCount)
            bytes.Add(Count);

        if (SectionData != null && SectionData.Length > 0)
        {
            bytes.AddRange(SectionData);
        }
        return bytes.ToArray();
    }

    [Flags]
    private enum MessageEncodingFlags : byte
    {
        None = 0,
        IndexIsUsed = 1,
        CountIsUsed = 2,
        ModuleNumberIsUsed = 4,
        VirtualSectionNumber = 8,
        SubSectionCount = 112, // 0x70
        MaskIsUsed = 128, // 0x80
    }

}
