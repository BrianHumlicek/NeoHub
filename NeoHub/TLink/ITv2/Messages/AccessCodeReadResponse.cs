using System.Text;
using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    /// <summary>
    /// Response for 0x4736 Access Code Read.
    /// Wire format: [NumberOfRecords:1] [UserNumber:1] [Reserved1:1] [Reserved2:1] [DataLength:1] [BCD PIN bytes...]
    /// </summary>
    [ITv2Command(ITv2Command.Response_Access_Code)]
    public record AccessCodeReadResponse : IMessageData
    {
        public byte NumberOfRecords { get; init; }
        public byte UserNumber { get; init; }
        public byte Reserved1 { get; init; }
        public byte Reserved2 { get; init; }

        /// <summary>
        /// BCD-encoded PIN bytes (length-prefixed on wire).
        /// Each byte encodes 2 digits: high nibble = tens, low nibble = units.
        /// 0xAA indicates an empty slot.
        /// </summary>
        [LeadingLengthArray]
        public byte[] BcdPinBytes { get; init; } = Array.Empty<byte>();

        /// <summary>
        /// BCD-decoded PIN code, or null if the slot is empty (0xAA).
        /// </summary>
        [IgnoreProperty]
        public string? PinCode
        {
            get
            {
                if (BcdPinBytes.Length == 0) return null;

                var sb = new StringBuilder(BcdPinBytes.Length * 2);
                foreach (var b in BcdPinBytes)
                {
                    if (b == 0xAA) return null;

                    int high = (b >> 4) & 0x0F;
                    int low = b & 0x0F;
                    if (high > 9 || low > 9) return null;

                    sb.Append((char)('0' + high));
                    sb.Append((char)('0' + low));
                }

                return sb.Length > 0 ? sb.ToString() : null;
            }
        }
    }
}
