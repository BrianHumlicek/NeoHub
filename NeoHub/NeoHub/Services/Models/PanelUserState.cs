namespace NeoHub.Services.Models
{
    /// <summary>
    /// In-memory representation of a panel user slot.
    /// </summary>
    public class PanelUserState
    {
        public int UserIndex { get; set; }
        public string? UserLabel { get; set; }
        public string? CodeValue { get; set; }
        public int? CodeLength { get; set; }
        public bool IsActive { get; set; }
        public bool HasProximityTag { get; set; }
        public PanelUserAttributes Attributes { get; set; }
        public List<byte> Partitions { get; set; } = new();

        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// User label read from the panel, or empty.
        /// </summary>
        public string DisplayLabel => UserLabel ?? "";
    }
}
