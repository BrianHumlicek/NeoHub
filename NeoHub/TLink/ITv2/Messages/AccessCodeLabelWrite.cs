using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.Serialization;
using System.Text;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(ITv2Command.Configuration_Write_Access_Codes_Label)]
    public record AccessCodeLabelWrite : CommandMessageBase
    {
        private const int LabelLength = 14;

        internal byte[] WireData
        {
            get => [..Header, ..Encoding.BigEndianUnicode.GetBytes("Test1         ")];
            set { }
        }
        [IgnoreProperty]
        public byte[] Header { get;init; } = Array.Empty<byte>();


        [IgnoreProperty]
        //[CompactInteger]
        public int AccessCodeStart { get; init; }
        //[CompactInteger]
        [IgnoreProperty]
        public int AccessCodeCount { get; init; }

        private string[] _labels = Array.Empty<string>();
        [IgnoreProperty]
        //[FixedLengthUnicodeStringArray]
        public string[] AccessCodeLabels
        {
            get => _labels;
            init => _labels = (value ?? Array.Empty<string>())
                .Select(s => (s ?? "").PadRight(LabelLength)[..LabelLength])
                .ToArray();
        }
    }
}
