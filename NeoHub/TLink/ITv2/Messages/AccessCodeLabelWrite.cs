using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.Serialization;
using System.Text;

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
    /// [Format           : 1B = 0x03 (UTF-16BE)]       — label encoding format
    /// Per label (AccessCodeCount times):
    ///   [LabelByteLength : 1B = 0x1C (28)]            — byte count of UTF-16BE data
    ///   [UTF-16BE data   : LabelByteLength bytes]     — label padded to 14 chars with spaces
    /// </summary>
    [ITv2Command(ITv2Command.Configuration_Write_Access_Codes_Label)]
    public record AccessCodeLabelWrite : CommandMessageBase
    {
        private const int LabelCharLength = 14;

        [CompactInteger]
        public int AccessCodeStart { get; init; }

        [CompactInteger]
        public int AccessCodeCount { get; init; }

        /// <summary>
        /// Label encoding format. Always 0x03 (UTF-16BE).
        /// </summary>
        public byte Format { get; init; } = 0x03;

        /// <summary>
        /// Per-label wire data: each label as [1-byte length][UTF-16BE bytes].
        /// Computed from <see cref="AccessCodeLabels"/>.
        /// </summary>
        public byte[] LabelData
        {
            get => EncodeLabelData();
            set { } // Write-only command; setter present for serializer compatibility.
        }

        /// <summary>
        /// The user-facing label strings. Each is padded/truncated to 14 characters.
        /// This property is not serialized directly; it feeds <see cref="LabelData"/>.
        /// </summary>
        [IgnoreProperty]
        public string[] AccessCodeLabels
        {
            get => _labels;
            init => _labels = (value ?? Array.Empty<string>())
                .Select(s => (s ?? "").PadRight(LabelCharLength)[..LabelCharLength])
                .ToArray();
        }

        private string[] _labels = Array.Empty<string>();

        private byte[] EncodeLabelData()
        {
            var result = new List<byte>();
            foreach (var label in _labels)
            {
                var encoded = Encoding.BigEndianUnicode.GetBytes(label);
                result.Add((byte)encoded.Length);
                result.AddRange(encoded);
            }
            return result.ToArray();
        }
    }
}
