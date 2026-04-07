using DSC.TLink.ITv2.Enumerations;

namespace DSC.TLink.ITv2.Messages
{
    /// <summary>
    /// Response for 0x4737 Access Code Attribute Read.
    /// Wire format: [NumberOfRecords:1] [UserNumber:1] [Reserved1:1] [Reserved2:1] [DataLength:1] [AttributeFlags:1]
    /// </summary>
    [ITv2Command(ITv2Command.Response_Access_Code_Attribute)]
    public record AccessCodeAttributeReadResponse : IMessageData
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
        /// Attribute flags byte. 0x00 = empty/disabled user.
        /// Bit 0 = Supervisor, Bit 1 = DuressCode, Bit 2 = CanBypassZone,
        /// Bit 3 = RemoteAccess, Bit 6 = BellSquawk, Bit 7 = OneTimeUse.
        /// </summary>
        public byte AttributeFlags { get; init; }
    }
}
