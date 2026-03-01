using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(ITv2Command.Notification_Life_Style_Zone_Status)]
    public record NotificationLifestyleZoneStatus : IMessageData
    {
        [CompactInteger]
        public byte ZoneNumber { get; init; }
        public LifeStyleZoneStatusCode Status { get; init; }
        public enum LifeStyleZoneStatusCode : byte
        {
            Unknown = 0xFF,
            Restored = 0,
            Open = 1
        }
    }
}
