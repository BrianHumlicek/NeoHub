namespace NeoHub.Services.Models
{
    /// <summary>
    /// In-memory representation of a panel access code slot.
    /// </summary>
    public class AccessCodeState
    {
        public int UserIndex { get; set; }
        public string? UserLabel { get; set; }
        public string? CodeValue { get; set; }
        public int? CodeLength { get; set; }
        public bool IsActive { get; set; }
        public bool HasProximityTag { get; set; }
        public AccessCodeAttributes Attributes { get; set; }
        public List<byte> Partitions { get; set; } = new();
        public byte[] RawAccessCode { get; set; } = Array.Empty<byte>();
        public byte[] RawAttributes { get; set; } = Array.Empty<byte>();
        public byte[] RawPartitionAssignments { get; set; } = Array.Empty<byte>();
        public byte[] RawConfiguration { get; set; } = Array.Empty<byte>();
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// User label read from the panel, or empty.
        /// </summary>
        public string DisplayLabel => UserLabel ?? "";
    }
}
