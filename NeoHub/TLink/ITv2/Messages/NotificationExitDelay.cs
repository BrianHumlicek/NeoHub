using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(ITv2Command.Notification_Exit_Delay)]
    public record NotificationExitDelay : IMessageData
    {
        [CompactInteger]
        public int Partition { get; init; }
        public ExitDelayFlags DelayFlags { get; init; }
        [CompactInteger]
        public int DurationInSeconds { get; init; }

        [IgnoreProperty]
        public bool AudibleDelay => (DelayFlags & ExitDelayFlags.AudibleExitDelay) != 0;
        [IgnoreProperty]
        public bool HasRestarted => (DelayFlags & ExitDelayFlags.ExitDelayHasRestarted) != 0;
        [IgnoreProperty]
        public bool IsUrgent => (DelayFlags & ExitDelayFlags.ExitDelayUrgency) != 0;
        [IgnoreProperty]
        public bool IsActive => (DelayFlags & ExitDelayFlags.ExitDelayActive) != 0;
        [IgnoreProperty]
        public TimeSpan Duration => TimeSpan.FromSeconds(DurationInSeconds);

        [Flags]
        public enum ExitDelayFlags : byte
        {
            AudibleExitDelay = 0x01,
            ExitDelayHasRestarted = 0x02,
            ExitDelayUrgency = 0x04,
            ExitDelayActive = 0x80,
        }
    }
}
