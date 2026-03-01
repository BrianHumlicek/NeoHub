using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.Serialization;


namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(ITv2Command.ModuleStatus_Zone_Status)]
    public record ModuleZoneStatus : IMessageData
    {
        [CompactInteger]
        public int ZoneStart { get; init; }
        [CompactInteger]
        public int ZoneCount { get; init; }
        public byte StatusSizeInBytes { get; init; }    //I think this should always be 1
        public ZoneStatusEnum[] ZoneStatusBytes { get; init; } = Array.Empty<ZoneStatusEnum>();

        [Flags]
        public enum ZoneStatusEnum : byte
        {
            Open = 0x01,
            Tamper = 0x02,
            Fault = 0x04,
            LowBattery = 0x08,
            Delinquency = 0x10,
            Alarm = 0x20,
            AlarmMemory = 0x40,
            Bypass = 0x80
        }
    }
}
