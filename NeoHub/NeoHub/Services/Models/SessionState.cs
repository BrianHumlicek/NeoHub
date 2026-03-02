namespace NeoHub.Services.Models
{
    /// <summary>
    /// Represents a complete session (panel connection) with its partitions and zones.
    /// </summary>
    public class SessionState
    {
        public required string SessionId { get; init; }
        public string Name => SessionId; // TODO: Allow friendly names
        public int MaxZones { get; set; }
        public int MaxPartitions { get; set; }
        public Dictionary<byte, PartitionState> Partitions { get; } = new();
        public Dictionary<byte, ZoneState> Zones { get; } = new();

        /// <summary>
        /// The panel's reported date/time at the moment of the last broadcast.
        /// </summary>
        public DateTime? PanelDateTime { get; set; }

        /// <summary>
        /// The local UTC time when PanelDateTime was received, used to calculate a running clock offset.
        /// </summary>
        public DateTime? PanelDateTimeSyncedAt { get; set; }

        /// <summary>
        /// Returns the estimated current panel time by adding elapsed time since the last sync.
        /// </summary>
        public DateTime? PanelDateTimeNow =>
            PanelDateTime.HasValue && PanelDateTimeSyncedAt.HasValue
                ? PanelDateTime.Value + (DateTime.UtcNow - PanelDateTimeSyncedAt.Value)
                : null;

        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}