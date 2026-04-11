using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    /// <summary>
    /// Request for 0x0736 Access Codes Read.
    /// Sent as a direct ITv2 command (extends CommandMessageBase) while the panel
    /// is in InstallersProgramming mode. The panel responds with 0x4736.
    /// </summary>
    [ITv2Command(ITv2Command.Configuration_Read_Access_Code)]
    public record AccessCodeReadRequest : CommandMessageBase
    {
        [CompactInteger]
        public int AccessCodeStart { get; init; }

        [CompactInteger]
        public int AccessCodeCount { get; init; }
    }
}
