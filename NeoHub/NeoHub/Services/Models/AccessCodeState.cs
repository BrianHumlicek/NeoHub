namespace NeoHub.Services.Models
{
    /// <summary>
    /// In-memory representation of a panel access code slot.
    /// Contains sensitive data (full code value) and must not be persisted.
    /// </summary>
    public class AccessCodeState
    {
        public int UserIndex { get; set; }
        public string? Label { get; set; }
        public string? CodeValue { get; set; }
        public int? CodeLength { get; set; }
        public bool IsActive { get; set; }
        public bool HasProximityTag { get; set; }
        public List<byte> Partitions { get; set; } = new();
        public byte[] RawAccessCode { get; set; } = Array.Empty<byte>();
        public byte[] RawAttributes { get; set; } = Array.Empty<byte>();
        public byte[] RawPartitionAssignments { get; set; } = Array.Empty<byte>();
        public byte[] RawConfiguration { get; set; } = Array.Empty<byte>();
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}
