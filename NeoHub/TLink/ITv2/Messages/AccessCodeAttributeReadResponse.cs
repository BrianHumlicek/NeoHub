using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    /// <summary>
    /// Response for 0x4737 Access Code Attribute Read.
    /// Wire format: [NumberOfRecords:1] [UserNumber:1] [Reserved1:1] [Reserved2:1] [DataLength:1] [AttributeFlags:1]
    /// </summary>
    [ITv2Command(ITv2Command.Response_Access_Code_Attribute)]
    public record AccessCodeAttributeReadResponse : IMessageData
    {
        [CompactInteger]
        public int AccessCodeStart { get; init; }
        [CompactInteger]
        public int AccessCodeCount { get; init; }
        public byte DataWidth { get; init; }    //Should always be 1
        public PanelUserAttributes[] Attributes { get; init; } = Array.Empty<PanelUserAttributes>();
        [Flags]
        public enum PanelUserAttributes : byte
        {
            None = 0x00,
            Supervisor = 0x01,  // Bit 0 — user has supervisor privileges
            DuressCode = 0x02,  // Bit 1 — code triggers silent duress alarm
            CanBypassZone = 0x04,  // Bit 2 — user can bypass zones
            RemoteAccess = 0x08,  // Bit 3 — user can arm/disarm remotely
                                  // Bits 4-5 unused/unknown
            BellSquawk = 0x40,  // Bit 6 — audible confirmation on arm/disarm
            OneTimeUse = 0x80,  // Bit 7 — code is single-use (auto-deleted after use)
        }

    }
}
