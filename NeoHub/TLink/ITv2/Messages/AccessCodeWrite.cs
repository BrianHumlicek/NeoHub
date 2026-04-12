using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(ITv2Command.Configuration_Write_Access_Code)]
    public record AccessCodeWrite : CommandMessageBase
    {
        [CompactInteger]
        public int AccessCodeStart { get; init; }
        [CompactInteger]
        public int AccessCodeCount { get; init; }
        [LeadingLengthBCDString]
        public string[] AccessCodes { get; init; } = Array.Empty<string>();
    }
}
