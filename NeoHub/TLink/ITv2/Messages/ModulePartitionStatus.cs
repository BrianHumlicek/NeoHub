using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(Enumerations.ITv2Command.ModuleStatus_Partition_Status)]
    public record  ModulePartitionStatus : IMessageData
    {
        [CompactInteger]
        public int Partition { get; init; }
        [CompactInteger]
        public int PartitionStatus { get; init; }
    }
}
