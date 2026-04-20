using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    /// <summary>
    /// Request for 0x0750 Access Codes Label Write.
    /// Sent as a direct ITv2 command (extends CommandMessageBase) while the panel
    /// is in AccessCodeProgramming mode.
    ///
    /// Wire format (after command word):
    /// [CommandSequence  : 1B]                          — from CommandMessageBase
    /// [AccessCodeStart  : CompactInteger]              — 1-based user index
    /// [AccessCodeCount  : CompactInteger]              — number of labels
    /// [Format           : 1B = 0x03 (UTF-16BE)]        — label encoding format
    /// Per label (AccessCodeCount times):
    ///   [LabelByteLength : 1B = 0x1C (28)]             — byte count of UTF-16BE data
    ///   [UTF-16BE data   : LabelByteLength bytes]      — label padded to 14 chars with spaces
    /// </summary>
    [ITv2Command(ITv2Command.Configuration_Write_Access_Codes_Label)]
    public record AccessCodeLabelWrite : CommandMessageBase
    {
        /// <summary>Required label length in characters (28 bytes UTF-16BE).</summary>
        public const int LabelCharLength = 14;

        [CompactInteger]
        public int AccessCodeStart { get; init; }

        [CompactInteger]
        public int AccessCodeCount { get; init; }

        /// <summary>
        /// Label encoding format. Always 0x03 (UTF-16BE).
        /// </summary>
        public byte Format { get; init; } = 0x03;

        /// <summary>
        /// User-facing label strings. Each label is normalized to exactly
        /// <see cref="LabelCharLength"/> characters: shorter labels are space-padded,
        /// longer labels are truncated.
        /// </summary>
        [UnicodeString]
        public string[] AccessCodeLabels
        {
            get => _accessCodeLabels;
            init => _accessCodeLabels = Normalize(value);
        }
        private readonly string[] _accessCodeLabels = Array.Empty<string>();

        private static string[] Normalize(string[]? labels)
        {
            if (labels is null || labels.Length == 0)
                return Array.Empty<string>();

            var result = new string[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                var s = labels[i] ?? string.Empty;
                result[i] = s.Length >= LabelCharLength
                    ? s[..LabelCharLength]
                    : s.PadRight(LabelCharLength);
            }
            return result;
        }
    }
}
