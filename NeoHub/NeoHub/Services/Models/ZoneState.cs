namespace NeoHub.Services.Models
{
    /// <summary>
    /// State of a single zone (belongs to session, associated with partitions).
    /// </summary>
    public class ZoneState
    {
        public byte ZoneNumber { get; set; }
        public string? DisplayNameLine1 { get; set; }
        public string? DisplayNameLine2 { get; set; }
        public string DisplayName =>
            (DisplayNameLine1, DisplayNameLine2) switch
            {
                (not null, not null) => $"{DisplayNameLine1} {DisplayNameLine2}",
                (not null, null) => DisplayNameLine1,
                _ => $"Zone {ZoneNumber}"
            };
        public bool IsOpen { get; set; }
        public bool IsFaulted { get; set; }
        public bool IsTampered { get; set; }
        public bool IsBypassed { get; set; }
        public List<byte> Partitions { get; set; } = new(); // Which partitions this zone is associated with
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}