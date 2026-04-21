using NeoHub.Services.Models;

namespace NeoHub.Services
{
    public record PanelUserWriteResult(bool Success, string? ErrorMessage = null)
    {
        /// <summary>
        /// True when the save crossed the enabled/disabled threshold.
        /// Callers (e.g. the edit dialog) should treat this as a "step 1 of 2" result:
        /// refresh their view from <see cref="UpdatedUser"/> and let the operator
        /// decide whether to make further edits rather than closing immediately.
        /// </summary>
        public bool Crossed { get; init; }

        /// <summary>
        /// The authoritative user state after the write, including a fresh re-read of
        /// attributes/partitions/config when the enabled/disabled threshold was crossed.
        /// Populated on success; may be null on failure.
        /// </summary>
        public PanelUserState? UpdatedUser { get; init; }
    }
}
