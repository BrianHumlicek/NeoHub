namespace NeoHub.Services.Models
{
    /// <summary>
    /// State of a single partition (no longer contains zones).
    /// </summary>
    public class PartitionState
    {
        public byte PartitionNumber { get; set; }
        public string? Name { get; set; }
        public string DisplayName => Name ?? $"Partition {PartitionNumber}";
        public bool IsReady { get; set; }
        public PartitionStatus Status { get; set; } = PartitionStatus.Unknown;
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        // Exit delay tracking
        public bool ExitDelayActive { get; set; }
        public DateTime? ExitDelayStartedAt { get; set; }
        public int ExitDelayDurationSeconds { get; set; }
        public bool ExitDelayAudible { get; set; }
        public bool ExitDelayUrgent { get; set; }

        /// <summary>
        /// Whether the partition is in any armed state (including arming during exit delay).
        /// </summary>
        public bool IsArmed => Status is PartitionStatus.ArmedAway
                                      or PartitionStatus.ArmedHome
                                      or PartitionStatus.ArmedNight
                                      or PartitionStatus.Arming;

        /// <summary>
        /// The effective status, accounting for exit delay.
        /// During exit delay, this returns Arming regardless of the stored Status.
        /// </summary>
        public PartitionStatus EffectiveStatus =>
            ExitDelayActive && ExitDelayRemaining.HasValue && ExitDelayRemaining.Value > TimeSpan.Zero
                ? PartitionStatus.Arming
                : Status;

        /// <summary>
        /// Returns the remaining exit delay time, or null if no delay is active.
        /// </summary>
        public TimeSpan? ExitDelayRemaining
        {
            get
            {
                if (!ExitDelayActive || !ExitDelayStartedAt.HasValue)
                    return null;

                var elapsed = DateTime.UtcNow - ExitDelayStartedAt.Value;
                var remaining = TimeSpan.FromSeconds(ExitDelayDurationSeconds) - elapsed;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }
    }
}