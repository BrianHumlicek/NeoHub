using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DSC.TLink
{
    public class PanelConfiguration
    {
        [Section("Partition Label", 000, 101)]
        public string[] PartitionLabels { get; set; } = Array.Empty<string>();
        [Section("Zone Label", 000, 001, 001)]
        public string[] ZoneLabels { get; set; } = Array.Empty<string>();
        [Section("Zone Definition", 001, 001)]
        public ZoneDefinition[] ZoneDefinitions { get; set; } = Array.Empty<ZoneDefinition>();
        [Section("Zone Attribute", 002, 001)]
        public ZoneAttributesIndex[] ZoneAttributes { get; set; } = Array.Empty<ZoneAttributesIndex>();
        [Section("Partition Enable", 200, 001)]
        public PartitionEnable[] PartitionEnables { get; set; } = Array.Empty<PartitionEnable>();
        //[201][001] Partition 1, bitmap of zones 1-8, [20][002] bitmap of zones 9-16, etc. up to 201 016 bitmap of zones 121-128
        //[202][001] Partition 2
        //...
        [Section("Partition Zone Assignments", 201, 001)]
        public byte[,] PartitionZoneAssignments { get; set; } = new byte[0, 0];
        public enum ZoneDefinition
        {
            NullZone = 0,
            Delay1 = 1,
            Delay2 = 2,
            Instant = 3,
            Interior = 4,
            InteriorStayAway = 5,
            DelayStayAway = 6,
            Delayed24HourFire = 7,
            Standard24HourFire = 8,
            InstantStayAway = 9,
            InteriorDelay = 10, // 0x0000000A
            DayZone = 11, // 0x0000000B
            NightZone = 12, // 0x0000000C
            Burglary24Hour = 17, // 0x00000011
            BellBuzzer24Hour = 18, // 0x00000012
            Supervisory24Hour = 23, // 0x00000017
            SupervisoryBuzzer24Hour = 24, // 0x00000018
            AutoVerifiedFire = 25, // 0x00000019
            FireSupervisory = 27, // 0x0000001B
            Gas24Hour = 40, // 0x00000028
            CO24Hour = 41, // 0x00000029
            Holdup24Hour = 42, // 0x0000002A
            Panic24Hour = 43, // 0x0000002B
            Heat24Hour = 45, // 0x0000002D
            Medical24Hour = 46, // 0x0000002E
            Emergency24Hour = 47, // 0x0000002F
            Sprinkler24Hour = 48, // 0x00000030
            Flood24Hour = 49, // 0x00000031
            LatchingTamper24Hour = 51, // 0x00000033
            NonAlarm24Hour = 52, // 0x00000034
            QuickBypass24Hour = 53, // 0x00000035
            HighTemperature24Hour = 56, // 0x00000038
            LowTemperature24Hour = 57, // 0x00000039
            NonLatchingTamper24Hour = 60, // 0x0000003C
            MomentaryKeyswitchArm = 66, // 0x00000042
            MaintainedKeyswitchArm = 67, // 0x00000043
            MomentaryKeyswitchDisarm = 68, // 0x00000044
            MaintainedKeyswitchDisarm = 69, // 0x00000045
            DoorBell = 71, // 0x00000047
        }
        [Flags]
        public enum ZoneAttributesIndex : ushort
        {
            IsAudible = 1,
            IsPulsedOrSteady = 2,
            IsDoorChime = IsPulsedOrSteady | IsAudible, // 0x00000003
            IsBypassable = 4,
            IsForceArmable = IsBypassable | IsAudible, // 0x00000005
            IsSwingerShutdown = IsBypassable | IsPulsedOrSteady, // 0x00000006
            IsTransmissionDelay = IsSwingerShutdown | IsAudible, // 0x00000007
            IsBurglaryVerified = 8,
            IsNormallyClosedLoop = IsBurglaryVerified | IsAudible, // 0x00000009
            IsSingleEOLResistor = IsBurglaryVerified | IsPulsedOrSteady, // 0x0000000A
            IsDoubleEOLResistor = IsSingleEOLResistor | IsAudible, // 0x0000000B
            IsFastLoopResponse = IsBurglaryVerified | IsBypassable, // 0x0000000C
            IsTwoWayAudio = IsFastLoopResponse | IsAudible, // 0x0000000D
            IsHoldupVerified = IsFastLoopResponse | IsPulsedOrSteady, // 0x0000000E
        }
        public enum PartitionEnable : byte
        {
            Disabled = 0,
            Enabled = 1
        }
        public class SectionAttribute : Attribute
        {
            public ushort[] SectionAddress { get; }
            public string Name { get; }
            public SectionAttribute(string name, params ushort[] sectionAddress)
            {
                Name = name;
                SectionAddress = sectionAddress;
            }
        }
    }
}
