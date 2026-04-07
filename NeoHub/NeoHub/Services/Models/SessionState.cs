using System.Collections.Concurrent;
using NeoHub.Services.PanelConfiguration;

namespace NeoHub.Services.Models
{
    /// <summary>
    /// Represents a complete session (panel connection) with its partitions and zones.
    /// </summary>
    public class SessionState
    {
        public required string SessionId { get; init; }
        public string Name => SessionId; // TODO: Allow friendly names
        public ConnectionPhase ConnectionPhase { get; set; }
        public int MaxZones { get; set; }
        public int MaxPartitions { get; set; }
        public int MaxUsers { get; set; }
        public Dictionary<byte, PartitionState> Partitions { get; } = new();
        public Dictionary<byte, ZoneState> Zones { get; } = new();
        public ConcurrentDictionary<int, AccessCodeState> AccessCodes { get; } = new();
        public DateTime? AccessCodesLastReadAt { get; set; }

        /// <summary>
        /// True while access code reading is in progress. Survives page refresh.
        /// </summary>
        public bool IsReadingAccessCodes { get; set; }

        /// <summary>
        /// Current reading phase description, e.g. "Reading user 12/48..."
        /// </summary>
        public string? AccessCodeReadProgress { get; set; }

        /// <summary>
        /// Number of users read so far.
        /// </summary>
        public int AccessCodeReadCurrent { get; set; }

        /// <summary>
        /// Total number of users to read.
        /// </summary>
        public int AccessCodeReadTotal { get; set; }

        /// <summary>
        /// Installer configuration data read via SectionRead.
        /// Null until a config read is explicitly triggered by the user.
        /// </summary>
        public PanelConfigurationState? Configuration { get; set; }

        /// <summary>
        /// Whether the panel is currently in installer programming mode.
        /// Tracked via ProgrammingLeadInOut notifications from the panel.
        /// </summary>
        public bool IsInProgrammingMode { get; set; }

        /// <summary>
        /// Whether the initial configuration pull (capabilities, labels, status)
        /// has completed. User-initiated operations should wait for this.
        /// </summary>
        public bool IsInitialized { get; set; }

        /// <summary>
        /// Serializes panel configuration operations (reads and writes).
        /// Prevents the initial handler pull and user-initiated operations from interleaving.
        /// </summary>
        public SemaphoreSlim ConfigLock { get; } = new(1, 1);

        /// <summary>
        /// When this session was established.
        /// </summary>
        public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the last message (send or receive) occurred on this session.
        /// </summary>
        public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

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
