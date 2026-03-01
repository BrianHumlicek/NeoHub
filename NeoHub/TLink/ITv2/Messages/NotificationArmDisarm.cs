using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(ITv2Command.Notification_Arming_Disarming)]
    public record NotificationArmDisarm : IMessageData
    {
        [CompactInteger]
        public int Partition { get; set; }
        public ArmingMode ArmMode { get; set; }
        public ArmDisarmMethod Method { get; set; }
        [CompactInteger]
        public int UserId { get; set; }
        public enum ArmDisarmMethod : byte
        {
            Unkown = 0,
            AccessCode = 1,
            FunctionKey = 2
        }
    }
}
