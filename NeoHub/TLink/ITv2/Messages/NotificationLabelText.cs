using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(ITv2Command.Configuration_Notification_Configuration)]
    public record NotificationLabelText : IMessageData
    {
        [CompactInteger]
        public LabelCollection Collection { get; init; }    //This seems odd that this would be a compact integer.
        [CompactInteger]
        public int Start { get; init; }
        [CompactInteger]
        public int End { get; init; }
        [FixedLengthUnicodeStringArray]
        public string[] Labels { get; init; } = Array.Empty<string>();
        public enum LabelCollection : byte
        {
            Zone = 0xD1,
            Partition = 0xD3,
            User = 0xD9
        }
    }
}
