using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    /// <summary>
    /// Request for 0x0737 Access Code Attributes Read.
    /// Sent as a direct ITv2 command (extends CommandMessageBase) while the panel
    /// is in InstallersProgramming mode. The panel responds with 0x4737.
    /// </summary>
    [ITv2Command(ITv2Command.Configuration_Read_Access_Code_Attribute)]
    public record AccessCodeAttributeReadRequest : CommandMessageBase
    {
        [CompactInteger]
        public int UserNumberStart { get; init; }
        
        [CompactInteger]
        public int NumberOfUsers { get; init; }
    }
}
