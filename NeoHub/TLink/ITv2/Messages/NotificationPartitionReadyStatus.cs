using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(ITv2Command.Notification_Partition_Ready_Status)]
    public record NotificationPartitionReadyStatus : IMessageData
    {
        [CompactInteger]
        public byte PartitionNumber { get; init; }
        public PartitionReadyStatusEnum Status { get; init; }
        public enum PartitionReadyStatusEnum : byte
        {
            Reserved = 0,
            ReadyToArm = 1,
            ReadyToForceArm = 2,
            NotReadyToArm = 3,
        }
    }
}
