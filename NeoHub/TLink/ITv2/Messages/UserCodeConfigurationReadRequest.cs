using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    /// <summary>
    /// Request for 0x073C User Code Configuration Read.
    /// Sent as a direct ITv2 command (extends CommandMessageBase) while the panel
    /// is in InstallersProgramming mode. The panel responds with 0x473C.
    /// </summary>
    [ITv2Command(ITv2Command.Configuration_Read_User_Code_Configuration)]
    public record UserCodeConfigurationReadRequest : CommandMessageBase
    {
        [CompactInteger]
        public int UserNumberStart { get; init; }
        
        [CompactInteger]
        public int NumberOfUsers { get; init; }
    }
}
