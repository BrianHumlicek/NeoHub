using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(ITv2Command.Configuration_Write_Access_Code_Partition_Assignment)]
    public record AccessCodePartitionAssignmentWrite : CommandMessageBase
    {
        [CompactInteger]
        public int AccessCodeStart { get; init; }

        [CompactInteger]
        public int AccessCodeCount { get; init; }

        /// <summary>
        /// Bytes per user's partition bitmask (e.g. 1 for ≤8 partitions, 2 for ≤16).
        /// </summary>
        public byte DataWidth { get; init; }

        /// <summary>
        /// Flat partition bitmask data: Count × DataWidth bytes.
        /// Each DataWidth-byte group is one user's LSB-first partition bitmask.
        /// </summary>
        public byte[] PartitionBitmask { get; init; } = Array.Empty<byte>();
    }
}
