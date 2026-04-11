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
        [CompactInteger]
        public int UserCodeStart { get; init; }
        [CompactInteger]
        public int UserCodeCount { get; init; }

        /// <summary>
        /// Length prefix for the data section (always 1).
        /// </summary>
        public byte DataWidth { get; init; }
        public UserCodeType[] CodeType { get; init; } = Array.Empty<UserCodeType>();
        public enum UserCodeType : byte
        {
            None = 0x00,
            Pin = 0x01,
            ProximityTag = 0x02
        }
    }
}
