using DSC.TLink.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(Enumerations.ITv2Command.Configuration_Installers_Section_Read)]
    public record SectionRead : CommandMessageBase
    {
        internal byte[] MessageBytes
        {
            get => EncodeMessageBytes();
            set => DecodeMessageBytes(value);
        }

        private MessageEncodingFlags _encoding;
        [IgnoreProperty]
        public int? Module { get; set; }
        [IgnoreProperty]
        public ushort Section { get; set; }
        [IgnoreProperty]
        public ushort[] Sections { get; set; } = Array.Empty<ushort>();
        [IgnoreProperty]
        public byte Index { get; set; } = 0;
        [IgnoreProperty]
        public byte Count { get; set; } = 1;
        [IgnoreProperty]
        internal byte[] SectionData { get; set; } = Array.Empty<byte>();

        private void DecodeMessageBytes(byte[] messageBytes)
        {
            int encodedLength = 3;
            if (messageBytes.Length < encodedLength) throw new ArgumentException("Message bytes too short to decode", nameof(messageBytes));

            int index = 0;

            _encoding = (MessageEncodingFlags)messageBytes[index++];

            if (_encoding.HasFlag(MessageEncodingFlags.ModuleNumberIsUsed))
                encodedLength++;

            int sectionsLength = ((byte)_encoding >> 4) & 0x07;
            encodedLength += sectionsLength * 2;

            if (_encoding.HasFlag(MessageEncodingFlags.IndexIsUsed))
                encodedLength++;
            if (_encoding.HasFlag(MessageEncodingFlags.CountIsUsed))
                encodedLength++;

            if (messageBytes.Length < encodedLength) throw new ArgumentException("Message bytes too short to decode", nameof(messageBytes));

            if (_encoding.HasFlag(MessageEncodingFlags.ModuleNumberIsUsed))
            {
                Module = messageBytes[index++];
            }

            Section = (ushort)(messageBytes[index++] << 8 | messageBytes[index++]);

            Sections = new ushort[sectionsLength];

            for (int i = 0; i < sectionsLength; i++)
            {
                Sections[i] = (ushort)(messageBytes[index++] << 8 | messageBytes[index++]);
            }

            if ( _encoding.HasFlag(MessageEncodingFlags.IndexIsUsed))
            {
                Index = messageBytes[index++];
            }

            if (_encoding.HasFlag(MessageEncodingFlags.CountIsUsed))
            {
                Count = messageBytes[index++];
            }

            SectionData = messageBytes.Skip(index).ToArray();
        }
        private byte[] EncodeMessageBytes()
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
            
            if (Sections.Length > 7)
                throw new InvalidOperationException("Cannot encode more than 8 subsections");
            
            encoding |= (MessageEncodingFlags)(Sections.Length << 4);
            
            bytes.Add((byte)encoding);

            if (Module.HasValue)
                bytes.Add((byte)Module.Value);

            bytes.Add((byte)(Section >> 8));
            bytes.Add((byte)(Section & 0xFF));

            foreach (var section in Sections)
            {
                bytes.Add((byte)(section >> 8));
                bytes.Add((byte)(section & 0xFF));
            }
            if (useIndex) bytes.Add(Index);
            if (useCount) bytes.Add((byte)(Count - 1));
            
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
}
