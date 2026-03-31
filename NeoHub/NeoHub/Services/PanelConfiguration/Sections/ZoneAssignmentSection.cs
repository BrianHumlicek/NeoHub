using DSC.TLink.ITv2.Messages;

namespace NeoHub.Services.PanelConfiguration.Sections;

/// <summary>
/// Partition-to-zone assignments from installer programming sections [201+].
/// Address: [201 + partition - 1][byteIdx] — bitmap encoding.
/// This section does not extend <see cref="SectionGroup{T}"/> because
/// its 2D bitmap structure and multi-read-per-item addressing are unique.
/// </summary>
public class ZoneAssignmentSection : IConfigSection
{
    private readonly PanelCapabilities _capabilities;
    private bool[,] _assignments = new bool[0, 0]; // [partition, zone] both 0-indexed

    public string DisplayName => "Zone Assignment";
    public bool IsSupported => true;
    public int MaxItems => _capabilities.MaxPartitions;

    /// <summary>
    /// Raw assignment matrix. _assignments[partitionIndex, zoneIndex] where both are 0-indexed.
    /// </summary>
    public bool[,] Values => _assignments;

    /// <summary>
    /// Snapshot of zone assignments grouped by partition, 1-indexed.
    /// Each entry contains the partition number and the list of assigned zone numbers.
    /// </summary>
    public IReadOnlyList<(int Partition, int[] Zones)> Items
    {
        get
        {
            var assignments = _assignments;
            int partitions = assignments.GetLength(0);
            int zones = assignments.GetLength(1);

            var result = new List<(int Partition, int[] Zones)>();
            for (int p = 0; p < partitions; p++)
            {
                var assignedZones = new List<int>();
                for (int z = 0; z < zones; z++)
                {
                    if (assignments[p, z])
                        assignedZones.Add(z + 1);
                }
                if (assignedZones.Count > 0)
                    result.Add((p + 1, assignedZones.ToArray()));
            }
            return result;
        }
    }

    public ZoneAssignmentSection(PanelCapabilities capabilities)
    {
        _capabilities = capabilities;
    }

    /// <summary>Formats the section address for a given partition (1-indexed).</summary>
    public string FormatAddress(int partition) => $"[{200 + partition:D3}]";

    public async Task ReadAllAsync(SendSectionRead send, CancellationToken ct)
    {
        int maxZones = _capabilities.MaxZones;
        int maxPartitions = _capabilities.MaxPartitions;
        int bytesPerPartition = (maxZones + 7) / 8;

        _assignments = new bool[maxPartitions, maxZones];

        for (int partition = 1; partition <= maxPartitions; partition++)
        {
            var data = new byte[bytesPerPartition];

            for (int byteIdx = 0; byteIdx < bytesPerPartition; byteIdx++)
            {
                var response = await send(
                    new SectionRead
                    {
                        SectionAddress = [(ushort)(200 + partition), (ushort)(1 + byteIdx)]
                    }, ct);

                if (response?.SectionData is { Length: >= 1 })
                    data[byteIdx] = response.SectionData[0];
            }

            DecodeBitmap(data, partition - 1, maxZones);
        }
    }

    public async Task ReadAsync(SendSectionRead send, int partition, CancellationToken ct)
    {
        int bytesPerPartition = (_capabilities.MaxZones + 7) / 8;
        var data = new byte[bytesPerPartition];

        for (int byteIdx = 0; byteIdx < bytesPerPartition; byteIdx++)
        {
            var response = await send(
                new SectionRead
                {
                    SectionAddress = [(ushort)(200 + partition), (ushort)(1 + byteIdx)]
                }, ct);

            if (response?.SectionData is { Length: >= 1 })
                data[byteIdx] = response.SectionData[0];
        }

        DecodeBitmap(data, partition - 1, _capabilities.MaxZones);
    }

    public async Task<SectionResult> WriteAsync(SendSectionWrite send, int partition, bool[] zones, CancellationToken ct)
    {
        int maxZones = _capabilities.MaxZones;
        int bytesPerPartition = (maxZones + 7) / 8;

        for (int byteIdx = 0; byteIdx < bytesPerPartition; byteIdx++)
        {
            byte bitmap = 0;
            for (int bit = 0; bit < 8; bit++)
            {
                int zoneIndex = byteIdx * 8 + bit;
                if (zoneIndex < zones.Length && zones[zoneIndex])
                    bitmap |= (byte)(0x80 >> bit);
            }

            var result = await send(
                new SectionWrite
                {
                    SectionAddress = [(ushort)(200 + partition), (ushort)(1 + byteIdx)],
                    SectionData = [bitmap]
                }, ct);

            if (!result.Success)
                return result;
        }

        int partIndex = partition - 1;
        for (int z = 0; z < Math.Min(zones.Length, maxZones); z++)
        {
            if (partIndex < _assignments.GetLength(0) && z < _assignments.GetLength(1))
                _assignments[partIndex, z] = zones[z];
        }

        return new(true);
    }

    /// <summary>Exports all assignment data as a contiguous bitmap buffer (partition-major).</summary>
    public byte[] Export()
    {
        int maxPartitions = _capabilities.MaxPartitions;
        int maxZones = _capabilities.MaxZones;
        int bytesPerPartition = (maxZones + 7) / 8;
        var data = new byte[maxPartitions * bytesPerPartition];

        for (int p = 0; p < maxPartitions; p++)
        {
            var bitmap = EncodeBitmap(p, maxZones);
            Buffer.BlockCopy(bitmap, 0, data, p * bytesPerPartition, bytesPerPartition);
        }

        return data;
    }

    /// <summary>Imports assignment data from a contiguous bitmap buffer (partition-major).</summary>
    public void Import(byte[] data)
    {
        int maxPartitions = _capabilities.MaxPartitions;
        int maxZones = _capabilities.MaxZones;
        int bytesPerPartition = (maxZones + 7) / 8;

        _assignments = new bool[maxPartitions, maxZones];

        for (int p = 0; p < maxPartitions; p++)
        {
            var partitionData = new byte[bytesPerPartition];
            int offset = p * bytesPerPartition;
            if (offset + bytesPerPartition <= data.Length)
                Buffer.BlockCopy(data, offset, partitionData, 0, bytesPerPartition);
            DecodeBitmap(partitionData, p, maxZones);
        }
    }

    /// <summary>Exports a single partition's assignment bitmap (1-indexed).</summary>
    public byte[] ExportItem(int item) => EncodeBitmap(item - 1, _capabilities.MaxZones);

    /// <summary>Imports a single partition's assignment bitmap (1-indexed).</summary>
    public void ImportItem(int item, byte[] data)
    {
        if (_assignments.GetLength(0) == 0)
            _assignments = new bool[_capabilities.MaxPartitions, _capabilities.MaxZones];
        DecodeBitmap(data, item - 1, _capabilities.MaxZones);
    }

    /// <summary>Returns a human-readable list of assigned zone numbers for a partition (1-indexed),
    /// preceded by the raw bitmap bytes shown as nibble-grouped bitfields.</summary>
    public string FormatItemValue(int item)
    {
        var bitmap = EncodeBitmap(item - 1, _capabilities.MaxZones);
        var bitmapStr = string.Join(" ", bitmap.Select(b =>
        {
            var bits = Convert.ToString(b, 2).PadLeft(8, '0');
            return $"{b:X2}[{bits[..4]} {bits[4..]}]";
        }));

        var zones = new List<int>();
        for (int z = 0; z < _capabilities.MaxZones; z++)
            if (item - 1 < _assignments.GetLength(0) && _assignments[item - 1, z])
                zones.Add(z + 1);

        var zoneStr = zones.Count > 0 ? string.Join(", ", zones) : "(none)";
        return $"{bitmapStr} → Zones: {zoneStr}";
    }

    /// <summary>Writes the current in-memory assignment for a single partition to the panel (1-indexed).</summary>
    public async Task<SectionResult> WriteItemAsync(SendSectionWrite send, int item, CancellationToken ct)
    {
        int maxZones = _capabilities.MaxZones;
        var zones = new bool[maxZones];
        for (int z = 0; z < maxZones; z++)
            zones[z] = _assignments[item - 1, z];
        return await WriteAsync(send, item, zones, ct);
    }

    private void DecodeBitmap(byte[] data, int partitionIndex, int maxZones)
    {
        for (int byteIndex = 0; byteIndex < data.Length; byteIndex++)
        {
            byte bitmap = data[byteIndex];
            for (int bit = 0; bit < 8; bit++)
            {
                int zoneIndex = byteIndex * 8 + bit;
                if (zoneIndex >= maxZones) return;

                _assignments[partitionIndex, zoneIndex] = (bitmap & (0x80 >> bit)) != 0;
            }
        }
    }

    private byte[] EncodeBitmap(int partitionIndex, int maxZones)
    {
        int bytesPerPartition = (maxZones + 7) / 8;
        var data = new byte[bytesPerPartition];

        for (int byteIndex = 0; byteIndex < bytesPerPartition; byteIndex++)
        {
            byte bitmap = 0;
            for (int bit = 0; bit < 8; bit++)
            {
                int zoneIndex = byteIndex * 8 + bit;
                if (zoneIndex < maxZones && _assignments[partitionIndex, zoneIndex])
                    bitmap |= (byte)(0x80 >> bit);
            }
            data[byteIndex] = bitmap;
        }

        return data;
    }
}
