using DSC.TLink.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;

namespace DSC.TLink.ITv2.Messages;

[ITv2Command(Enumerations.ITv2Command.Configuration_Installers_Section_Read)]
public record SectionRead : CommandMessageBase
{
    private readonly SectionMessageSerializer _sectionSerializer = new();
    internal byte[] MessageBytes
    {
        get => _sectionSerializer.MessageBytes;
        set => _sectionSerializer.MessageBytes = value;
    }
    [IgnoreProperty]
    public int? Module { get => _sectionSerializer.Module; set => _sectionSerializer.Module = value; }
    [IgnoreProperty]
    public ushort[] SectionAddress { get => _sectionSerializer.SectionAddress; set => _sectionSerializer.SectionAddress = value; }
    [IgnoreProperty]
    public byte Index { get => _sectionSerializer.Index; set => _sectionSerializer.Index = value; }
    [IgnoreProperty]
    public byte Count { get => _sectionSerializer.Count; set => _sectionSerializer.Count = value; }
}
