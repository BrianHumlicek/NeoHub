using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(ITv2Command.Configuration_Write_Access_Code_Attribute)]
    public record AccessCodeAttributeWrite : CommandMessageBase
    {
        [CompactInteger]
        public int AccessCodeStart { get; init; }
        [CompactInteger]
        public int AccessCodeCount { get; init; }
        public byte DataWidth { get; init; }    //Should always be 1
        public PanelUserAttributes[] Attributes { get; init; } = Array.Empty<PanelUserAttributes>();
    }
}
