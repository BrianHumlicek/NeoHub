using DSC.TLink.ITv2.Enumerations;

namespace NeoHub.Services.Models
{
    /// <summary>
    /// In-memory representation of a panel user slot.
    /// </summary>
    public class PanelUserState
    {
        /// <summary>
        /// Returns the canonical disabled-access-code sentinel for a panel of the given
        /// configured code length (all 'A' nibbles, i.e. <c>0xAA</c> bytes on the wire).
        /// Valid lengths are 4, 6, or 8.
        /// </summary>
        public static string DisabledAccessCode(int codeLength) => codeLength switch
        {
            4 or 6 or 8 => new string('A', codeLength),
            _ => throw new ArgumentException(
                $"Invalid access code length {codeLength}; must be 4, 6, or 8", nameof(codeLength))
        };

        /// <summary>
        /// Returns true if <paramref name="code"/> is the disabled sentinel for any supported
        /// code length — a non-empty string of all 'A' characters with length 4, 6, or 8.
        /// This is the single source of truth for "is this slot disabled on the panel."
        /// </summary>
        public static bool IsDisabledCode(string? code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            if (code.Length != 4 && code.Length != 6 && code.Length != 8) return false;
            foreach (var ch in code)
                if (ch != 'A' && ch != 'a') return false;
            return true;
        }

        public int UserIndex { get; set; }
        public string? UserLabel { get; set; }
        public string? CodeValue { get; set; }
        public int? CodeLength { get; set; }
        public bool HasProximityTag { get; set; }
        public PanelUserAttributes Attributes { get; set; }
        public List<byte> Partitions { get; set; } = new();

        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// True when the user slot is explicitly disabled via an all-'A' sentinel access code.
        /// </summary>
        public bool IsDisabled => IsDisabledCode(CodeValue);

        /// <summary>
        /// True when the slot has a real, non-disabled access code.
        /// An empty / unread slot is neither active nor disabled.
        /// </summary>
        public bool IsActive => !string.IsNullOrEmpty(CodeValue) && !IsDisabled;

        /// <summary>
        /// User label read from the panel, or empty.
        /// </summary>
        public string DisplayLabel => UserLabel ?? "";
    }
}
