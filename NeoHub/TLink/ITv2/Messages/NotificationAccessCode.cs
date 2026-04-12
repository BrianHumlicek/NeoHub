using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(Enumerations.ITv2Command.Notification_Access_Code_Update)]
    public record NotificationAccessCode : IMessageData
    {
        public Parameter UpdatedParameter { get; init; }
        [CompactInteger]
        public int NumOfRecordsUpdated { get; init; }
        [CompactInteger]
        public int Start { get; init; }
        [CompactInteger]
        public int End { get; init; }
        [Flags]
        public enum Parameter : byte
        {
            AccessCode = 1,
            Attributes = 2,
            PartitionAssignment = 4,
            Label = 8
        }
    }
}
