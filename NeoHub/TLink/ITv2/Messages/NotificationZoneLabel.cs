using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(Enumerations.ITv2Command.Configuration_Notification_Configuration)]
    public record NotificationZoneLabel : IMessageData
    {
        [CompactInteger]
        public int Unknown { get; init; }
        [CompactInteger]
        public int ZoneStart { get; init; }
        [CompactInteger]
        public int ZoneCount { get; init; }
        [FixedLengthUnicodeStringArray]
        public string[] Labels { get; init; } = Array.Empty<string>();
    }
}
