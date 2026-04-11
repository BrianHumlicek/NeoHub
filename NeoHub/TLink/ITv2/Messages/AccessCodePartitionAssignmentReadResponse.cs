using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    /// <summary>
    /// Response for 0x4738 Access Code Partition Assignment Read.
    /// Wire format: [Start:CompactInt] [Count:CompactInt] [DataWidth:1] [Bitmask bytes: Count × DataWidth]
    /// </summary>
    [ITv2Command(ITv2Command.Response_Access_Code_Partition_Assignment)]
    public record AccessCodePartitionAssignmentReadResponse : IMessageData
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

        /// <summary>
        /// Per-user partition assignments decoded from the flat bitmask.
        /// Indexed by batch position (0 = first user). Each element contains 1-indexed partition numbers.
        /// </summary>
        [IgnoreProperty]
        public List<byte>[] PartitionAssignments => Enumerable.Range(0, AccessCodeCount)
            .Select(GetPartitionsForUser)
            .ToArray();

        private List<byte> GetPartitionsForUser(int index)
        {
            var result = new List<byte>();
            if (DataWidth == 0) return result;
            int offset = index * DataWidth;
            for (int i = 0; i < DataWidth && offset + i < PartitionBitmask.Length; i++)
            {
                byte bitmap = PartitionBitmask[offset + i];
                for (int bit = 0; bit < 8; bit++)
                {
                    if ((bitmap & (1 << bit)) != 0)
                    {
                        int partition = i * 8 + bit + 1;
                        if (partition <= byte.MaxValue)
                            result.Add((byte)partition);
                    }
                }
            }
            return result;
        }
    }
}
