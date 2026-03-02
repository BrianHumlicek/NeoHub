using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(Enumerations.ITv2Command.ModuleStatus_Partition_Status)]
    public record ModulePartitionStatus : IMessageData
    {
        [CompactInteger]
        public int Partition { get; init; }
        [LeadingLengthArray]
        public byte[] PartitionStatus { get; init; } = Array.Empty<byte>();
        [IgnoreProperty]
        public PartitionStatus1 Status1 => PartitionStatus.Length > 0 ? (PartitionStatus1)PartitionStatus[0] : 0;
        [IgnoreProperty]
        public PartitionStatus2 Status2 => PartitionStatus.Length > 1 ? (PartitionStatus2)PartitionStatus[1] : 0;
        [IgnoreProperty]
        public PartitionStatus3 Status3 => PartitionStatus.Length > 2 ? (PartitionStatus3)PartitionStatus[2] : 0;
        [Flags]
        public enum PartitionStatus1 : byte
        {
            Armed = 0x01,
            ReadyToArm = 0x02,  //StayArmed
            StayArmed = 0x03,
            ReadyToForceArm = 0x04, //ArmedAway
            ArmedAway = 0x05,
            NotReadyToArm = 0x08,  //NightArmed
            NightArmed = 0x09,
            WalkTestMode = 0x10,    //ArmedWithNoEntryDelay
            ArmedWithNoEntryDelay = 0x11,
            ExitDelayInProgress = 0x20,
            EntryDelayInProgress = 0x40,
            QuickExitInProgress = 0x80,
        }
        [Flags]
        public enum PartitionStatus2 : byte
        {
            PartitionInAlarm = 0x01,
            TroublesPresent = 0x02,
            ZonesBypassed = 0x04,
            InProgrammingMode = 0x08,
            AlarmsInMemory = 0x10,
            DoorChimeEnabled = 0x20,
            AudibleBellSiren = 0x40,
            AudibleKeypadBuzzerAlarm = 0x80
        }
        [Flags]
        public enum PartitionStatus3 : byte
        {
            FirePreAlertInProgress = 0x01
        }
    }
}
