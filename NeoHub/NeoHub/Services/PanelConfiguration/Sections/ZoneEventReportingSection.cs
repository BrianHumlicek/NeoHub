using DSC.TLink.ITv2.Messages;

namespace NeoHub.Services.PanelConfiguration.Sections
{
    public class ZoneEventReportingSection
    {
        private readonly PanelCapabilities _capabilities;
        private ZoneEventReportingAttributes[] _values = [];

        public IReadOnlyList<ZoneEventReportingAttributes> Values => _values;

        /// <summary>Snapshot of zones with attributes set (filters out zero), 1-indexed.</summary>
        public IReadOnlyList<(int Number, ZoneEventReportingAttributes Value)> Items
        {
            get
            {
                var values = _values;
                return values
                    .Select((v, i) => (Number: i + 1, Value: v))
                    .ToList();
            }
        }

        public ZoneEventReportingSection(PanelCapabilities capabilities)
        {
            _capabilities = capabilities;
        }

        public async Task ReadAllAsync(SendSectionRead send, CancellationToken ct)
        {
            _values = new ZoneEventReportingAttributes[_capabilities.MaxZones];

            var response = await send(new SectionRead { SectionAddress = [307, 1], Count = (byte)_capabilities.MaxZones }, ct);
            if (response?.SectionData is not null)
            {
                int elementCount = Math.Min(response.SectionData.Length, _values.Length);
                for (int i = 0; i < elementCount; i++)
                    _values[i] = (ZoneEventReportingAttributes)response.SectionData[i];
            }
        }

        public async Task ReadAsync(SendSectionRead send, int zone, CancellationToken ct)
        {
            var response = await send(new SectionRead { SectionAddress = [307, (ushort)zone] }, ct);
            if (response?.SectionData is { Length: >= 2 })
                _values[zone - 1] = (ZoneEventReportingAttributes)response.SectionData[0];
        }

        public async Task<SectionResult> WriteAsync(SendSectionWrite send, int zone, ZoneEventReportingAttributes attributes, CancellationToken ct)
        {
            var result = await send(new SectionWrite { SectionAddress = [307, (ushort)zone], SectionData = [(byte)attributes] }, ct);
            if (result.Success)
                _values[zone - 1] = attributes;
            return result;
        }
    }
    [Flags]
    public enum ZoneEventReportingAttributes : byte
    {
        Alarm = 0x01,
        AlarmRestore = 0x02,
        Tamper = 0x04,
        TamperRestore = 0x08,
        Fault = 0x10,
        FaultRestore = 0x20
    }
}
