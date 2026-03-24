using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(Enumerations.ITv2Command.Response_Installers_Section_Read)]
    public record SectionReadResponse : IMessageData
    {
        internal byte[] MessageBytes
        {
            get => SectionReadCommand.MessageBytes;
            set => SectionReadCommand.MessageBytes = value;
        }
        [IgnoreProperty]
        public SectionRead SectionReadCommand { get; } = new SectionRead();
        [IgnoreProperty]
        public byte[] SectionData => SectionReadCommand.SectionData;
    }
}
