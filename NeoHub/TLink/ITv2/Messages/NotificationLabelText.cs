using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(Enumerations.ITv2Command.Configuration_Notification_Configuration)]
    public record NotificationLabelText : IMessageData
    {
        [CompactInteger]
        public int Unknown { get; init; }   //D1 seems to get zone labels and D3 seems to get partition labels
        [CompactInteger]
        public int Start { get; init; }
        [CompactInteger]
        public int End { get; init; }
        [FixedLengthUnicodeStringArray]
        public string[] Labels { get; init; } = Array.Empty<string>();
    }
}
