using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    /// <summary>
    /// Response for 0x4738 Access Code Partition Assignment Read.
    /// Wire format: [NumberOfRecords:1] [UserNumber:1] [Reserved1:1] [Reserved2:1] [DataLength:1] [Bitmask bytes...]
    /// </summary>
    [ITv2Command(ITv2Command.Response_Access_Code_Partition_Assignment)]
    public record AccessCodePartitionAssignmentReadResponse : IMessageData
    {
        [CompactInteger]
        public int AccessCodeStart { get; init; }

        [CompactInteger]
        public int AccessCodeCount { get; init; }

        /// <summary>
        /// Partition assignment bitmask bytes (length-prefixed on wire).
        /// LSB-first: bit N of byte M = partition (M*8 + N + 1).
        /// </summary>
        [LeadingLengthArray]
        public byte[] PartitionBitmask { get; init; } = Array.Empty<byte>();

        /// <summary>
        /// Partition assignments decoded from bitmask (1-indexed partition numbers).
        /// </summary>
        [IgnoreProperty]
        public List<byte> AssignedPartitions
        {
            get
            {
                var result = new List<byte>();
                for (int i = 0; i < PartitionBitmask.Length; i++)
                {
                    byte bitmap = PartitionBitmask[i];
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
}
