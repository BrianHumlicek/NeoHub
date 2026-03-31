using DSC.TLink.ITv2.Messages;

namespace NeoHub.Services.PanelConfiguration.Sections;

/// <summary>
/// Non-generic interface shared by all configuration sections (both <see cref="SectionGroup{T}"/>
/// and standalone sections like <see cref="ZoneAssignmentSection"/>). Enables generic iteration
/// for reading, exporting, importing, diffing, and UI enumeration.
/// </summary>
public interface IConfigSection
{
    string DisplayName { get; }
    bool IsSupported { get; }
    int MaxItems { get; }
    string FormatAddress(int item);
    string FormatItemValue(int item);
    Task ReadAllAsync(SendSectionRead send, CancellationToken ct);
    Task<SectionResult> WriteItemAsync(SendSectionWrite send, int item, CancellationToken ct);
    byte[] Export();
    void Import(byte[] data);
    byte[] ExportItem(int item);
    void ImportItem(int item, byte[] data);
}

/// <summary>
/// Abstract base for a panel configuration section that stores an array of <typeparamref name="T"/> values
/// read/written via SectionRead/SectionWrite commands. Subclasses provide serialization,
/// addressing, and display metadata; the base handles R/W protocol, export/import, and
/// common properties (<see cref="Values"/>, <see cref="Items"/>, <see cref="FormatAddress"/>).
/// </summary>
public abstract class SectionGroup<T> : IConfigSection
{
    protected readonly PanelCapabilities Capabilities;
    private T[] _values = [];

    protected SectionGroup(PanelCapabilities capabilities)
    {
        Capabilities = capabilities;
    }

    /// <summary>Display name for UI labels, file keys, and print headers.</summary>
    public abstract string DisplayName { get; }

    /// <summary>
    /// Number of elements in the section. Defaults to 1 for single-element sections.
    /// Multi-element sections should override with <c>Capabilities.MaxZones</c> etc.
    /// </summary>
    public virtual int MaxItems => 1;

    /// <summary>Whether this section is supported by the connected panel.</summary>
    public virtual bool IsSupported => true;

    /// <summary>
    /// Returns the full section address for a 1-indexed item number.
    /// For single-element sections, the item parameter can be ignored.
    /// </summary>
    protected abstract ushort[] GetItemAddress(int item);

    /// <summary>
    /// Deserializes a bulk <see cref="SectionReadResponse.SectionData"/> buffer
    /// into an array of <typeparamref name="T"/>. The <paramref name="count"/>
    /// parameter indicates the expected number of elements.
    /// </summary>
    protected abstract T[] DeserializeAll(byte[] data, int count);

    /// <summary>
    /// Serializes an array of <typeparamref name="T"/> into a contiguous byte buffer
    /// suitable for file export or comparison.
    /// </summary>
    protected abstract byte[] SerializeAll(T[] values);

    /// <summary>Raw value array (0-indexed).</summary>
    public IReadOnlyList<T> Values => _values;

    /// <summary>Snapshot of items (1-indexed number + value).</summary>
    public virtual IReadOnlyList<(int Number, T Value)> Items
    {
        get
        {
            var values = _values;
            return values
                .Select((v, i) => (Number: i + 1, Value: v))
                .ToList();
        }
    }

    /// <summary>
    /// Formats the section address for a 1-indexed item as a display string.
    /// Example: <c>[001][003]</c> for zone 3 of section 001.
    /// </summary>
    public string FormatAddress(int item)
    {
        return string.Concat(GetItemAddress(item).Select(a => $"[{a:D3}]"));
    }

    /// <summary>Gets or sets a single value by 1-indexed item number.</summary>
    public T this[int item]
    {
        get => _values[item - 1];
        set => _values[item - 1] = value;
    }

    /// <summary>
    /// Reads all items from the panel in a single bulk request.
    /// Override for sections that require batching or special addressing.
    /// </summary>
    public virtual async Task ReadAllAsync(SendSectionRead send, CancellationToken ct)
    {
        _values = new T[MaxItems];

        var response = await send(
            new SectionRead { SectionAddress = GetItemAddress(1), Count = (byte)MaxItems }, ct);

        if (response?.SectionData is not null)
            _values = DeserializeAll(response.SectionData, MaxItems);
    }

    /// <summary>Reads a single item by 1-indexed number.</summary>
    public virtual async Task ReadAsync(SendSectionRead send, int item, CancellationToken ct)
    {
        EnsureCapacity();

        var response = await send(
            new SectionRead { SectionAddress = GetItemAddress(item) }, ct);

        if (response?.SectionData is not null)
        {
            var parsed = DeserializeAll(response.SectionData, 1);
            if (parsed.Length > 0)
                _values[item - 1] = parsed[0];
        }
    }

    /// <summary>Writes a single item to the panel by 1-indexed number.</summary>
    public virtual async Task<SectionResult> WriteAsync(
        SendSectionWrite send, int item, T value, CancellationToken ct)
    {
        EnsureCapacity();
        var data = SerializeAll([value]);
        var result = await send(
            new SectionWrite { SectionAddress = GetItemAddress(item), SectionData = data }, ct);

        if (result.Success)
            _values[item - 1] = value;

        return result;
    }

    /// <summary>Exports all values as a raw byte buffer for file save.</summary>
    public byte[] Export() => SerializeAll(_values);

    /// <summary>Imports values from a raw byte buffer (file load).</summary>
    public void Import(byte[] data) => _values = DeserializeAll(data, MaxItems);

    /// <summary>Exports a single item's bytes (1-indexed).</summary>
    public byte[] ExportItem(int item) => SerializeAll([this[item]]);

    /// <summary>Imports a single item from bytes (1-indexed).</summary>
    public void ImportItem(int item, byte[] data)
    {
        EnsureCapacity();
        var parsed = DeserializeAll(data, 1);
        if (parsed.Length > 0)
            _values[item - 1] = parsed[0];
    }

    /// <summary>Returns a human-readable string for the item value at a 1-indexed position.</summary>
    public virtual string FormatItemValue(int item) => this[item]?.ToString() ?? "";

    /// <summary>Writes the current in-memory value for a single item to the panel (1-indexed).</summary>
    public Task<SectionResult> WriteItemAsync(SendSectionWrite send, int item, CancellationToken ct)
        => WriteAsync(send, item, this[item], ct);

    private void EnsureCapacity()
    {
        if (_values.Length < MaxItems)
            Array.Resize(ref _values, MaxItems);
    }
}
