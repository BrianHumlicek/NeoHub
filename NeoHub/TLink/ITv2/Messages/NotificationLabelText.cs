using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(ITv2Command.Configuration_Notification_Configuration)]
    public record NotificationLabelText : IMessageData
    {
        [CompactInteger]
        public int LabelType { get; init; }
        [CompactInteger]
        public int Start { get; init; }
        [CompactInteger]
        public int End { get; init; }
        [FixedLengthUnicodeStringArray]
        public string[] Labels { get; init; } = Array.Empty<string>();
    }
}
