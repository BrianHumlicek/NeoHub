using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(Enumerations.ITv2Command.Configuration_Panel_Programming_Lead_InOut)]
    public record ProgrammingLeadInOut : IMessageData
    {
        [CompactInteger]
        public int Partition {  get; init; }
        public ProgrammingType Programming { get; init; }
        [CompactInteger]
        public int UserCode { get; init; }
        public ProgrammingLeadInOutAccess Access { get; init; }
        public ProgrammingMode Mode { get; init; }
        public DateTime DateTime { get; init; }

        public enum ProgrammingType : byte
        {
            LeadIn = 0,
            LeadOut = 1
        }
        public enum ProgrammingLeadInOutAccess : byte
        {
            ConfigurationThroughInteractiveServices = 0,
            ForFutureUse = 1,
            ConfigurationThroughKeypad = 2,
            Dls = 3,
            C24 = 4
        }
        public enum ProgrammingMode : byte
        {
            InstallersProgramming = 0,
            AccessCodeProgramming = 1,
            UserFunctionProgramming = 2,
            UserBypassProgramming = 3
        }

    }
}
