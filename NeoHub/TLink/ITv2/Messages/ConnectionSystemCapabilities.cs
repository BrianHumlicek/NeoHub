using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(Enumerations.ITv2Command.Connection_System_Capabilities)]
    public record ConnectionSystemCapabilities : IMessageData
    {
        [CompactInteger]
        public int MaxZones { get; init; }
        [CompactInteger]
        public int MaxUsers { get; init; }
        [CompactInteger]
        public int MaxPartitions { get; init; }
        [CompactInteger]
        public int MaxFOBs { get; init; }
        [CompactInteger]
        public int MaxProxTags { get; init; }
        [CompactInteger]
        public int MaxOutputs { get; init; }
    }
}
