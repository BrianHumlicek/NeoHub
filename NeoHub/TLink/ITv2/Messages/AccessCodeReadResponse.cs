using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    /// <summary>
    /// Response for 0x4736 Access Code Read.
    /// Wire format: [Start:CompactInt] [Count:CompactInt] [BCDByteLength:1] [BCD₁:BCDByteLength] ... ×Count
    /// </summary>
    [ITv2Command(ITv2Command.Response_Access_Code)]
    public record AccessCodeReadResponse : IMessageData
    {
        [CompactInteger]
        public int AccessCodeStart { get; init; }
        [CompactInteger]
        public int AccessCodeCount { get; init; }
        [LeadingLengthBCDString]
        public string[] AccessCodes { get; init; } = Array.Empty<string>();
    }
}
