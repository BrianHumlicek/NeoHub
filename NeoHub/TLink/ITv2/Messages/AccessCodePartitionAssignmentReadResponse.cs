using DSC.TLink.ITv2.Enumerations;

namespace DSC.TLink.ITv2.Messages
{
    /// <summary>
    /// Raw payload wrapper for 0x4738 Access Code Partition Assignments Read Response.
    /// </summary>
    [ITv2Command(ITv2Command.Response_Access_Code_Partition_Assignment)]
    public record AccessCodePartitionAssignmentReadResponse : IMessageData
    {
        public byte[] Data { get; init; } = Array.Empty<byte>();
    }
}
