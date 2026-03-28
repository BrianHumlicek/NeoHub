using DSC.TLink.ITv2.Messages;

namespace NeoHub.Services.PanelConfiguration.Sections;

/// <summary>
/// Partition-to-zone assignments from installer programming sections [201+].
/// Address: [201 + partition - 1][001] with Count = ceil(MaxZones / 8).
/// Each response byte is a bitmap: bit 7 = first zone in that byte, bit 6 = second, etc.
/// </summary>
public class ZoneAssignmentSection
{
    private readonly PanelCapabilities _capabilities;
    private bool[,] _assignments = new bool[0, 0]; // [partition, zone] both 0-indexed

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

    private void DecodeBitmap(byte[] data, int partitionIndex, int maxZones)
    {
        for (int byteIndex = 0; byteIndex < data.Length; byteIndex++)
        {
            byte bitmap = data[byteIndex];
            for (int bit = 0; bit < 8; bit++)
            {
                int zoneIndex = byteIndex * 8 + bit;
                if (zoneIndex >= maxZones) return;

                // Bit 7 = first zone in the byte, bit 6 = second, etc.
                _assignments[partitionIndex, zoneIndex] = (bitmap & (0x80 >> bit)) != 0;
            }
        }
    }

    /// <summary>
    /// Writes the zone assignment bitmap for a single partition.
    /// Each byte is written individually to match the read pattern.
    /// </summary>
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

        // Update local state on success
        int partIndex = partition - 1;
        for (int z = 0; z < Math.Min(zones.Length, maxZones); z++)
        {
            if (partIndex < _assignments.GetLength(0) && z < _assignments.GetLength(1))
                _assignments[partIndex, z] = zones[z];
        }

        return new(true);
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
