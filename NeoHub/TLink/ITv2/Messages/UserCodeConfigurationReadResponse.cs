using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    /// <summary>
    /// Response for 0x473C User Code Configuration Read.
    /// Wire format: [NumberOfRecords:1] [UserNumber:1] [Reserved1:1] [Reserved2:1] [DataLength:1] [CodeType:1]
    /// </summary>
    [ITv2Command(ITv2Command.Response_User_Code_Configuration)]
    public record UserCodeConfigurationReadResponse : IMessageData
    {
        public byte NumberOfRecords { get; init; }
        public byte UserNumber { get; init; }
        public byte Reserved1 { get; init; }
        public byte Reserved2 { get; init; }

        /// <summary>
        /// Length prefix for the data section (always 1).
        /// </summary>
        public byte DataLength { get; init; }

        /// <summary>
        /// Code type byte. 0x00=none, 0x01=PIN, 0x02=proximity tag.
        /// </summary>
        public byte CodeType { get; init; }

        /// <summary>
        /// Whether this user has a proximity tag (code type 0x02).
        /// </summary>
        [IgnoreProperty]
        public bool HasProximityTag => CodeType == 0x02;
    }
}
