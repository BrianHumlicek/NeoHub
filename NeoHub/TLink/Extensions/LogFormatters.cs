// DSC TLink - a communications library for DSC Powerseries NEO alarm panels
// Copyright (C) 2024 Brian Humlicek
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Reflection;
using System.Text;
using DSC.TLink.ITv2.Messages;

namespace DSC.TLink.Extensions;

/// <summary>
/// Lazy hex formatter for byte data.
/// Defers all formatting to <see cref="ToString"/> so the logging pipeline incurs zero cost
/// when the log level is disabled.
/// Usage: _logger.LogTrace("TX {Bytes}", new HexBytes(data));
/// To get a string eagerly (e.g. for error messages): new HexBytes(data).ToString()
/// </summary>
public readonly struct HexBytes
{
    private readonly ReadOnlyMemory<byte> _data;

    public HexBytes(byte[] data) => _data = data;
    public HexBytes(ReadOnlyMemory<byte> data) => _data = data;

    public override string ToString()
    {
        var span = _data.Span;
        if (span.IsEmpty) return "[]";

        var sb = new StringBuilder(span.Length * 3 + 1);
        sb.Append('[');
        for (int i = 0; i < span.Length; i++)
        {
            if (i > 0) sb.Append('-');
            sb.Append(span[i].ToString("X2"));
        }
        sb.Append(']');
        return sb.ToString();
    }
}

/// <summary>
/// Hex editor-style grid formatter for larger byte arrays.
/// Produces 16 bytes per row with a mid-row space, a leading offset column, and a length header.
/// Usage: new HexGrid(data).ToString()
/// </summary>
public readonly struct HexGrid
{
    private const int BytesPerRow = 16;
    private const int HalfRow = BytesPerRow / 2;
    private readonly ReadOnlyMemory<byte> _data;

    public HexGrid(byte[] data) => _data = data;
    public HexGrid(ReadOnlyMemory<byte> data) => _data = data;

    public override string ToString()
    {
        var span = _data.Span;
        if (span.IsEmpty) return "0 bytes";

        // header: "N bytes"
        // each row: "XXXX  XX XX ... XX  XX XX ... XX\n"
        // offset(4) + 2 spaces + 8*(XX + space) + 1 extra space + 8*(XX + space)
        var sb = new StringBuilder();
        sb.AppendLine($"{span.Length} bytes");

        for (int row = 0; row < span.Length; row += BytesPerRow)
        {
            sb.Append(row.ToString("X4"));
            sb.Append("  ");

            for (int col = 0; col < BytesPerRow; col++)
            {
                if (col == HalfRow) sb.Append(' ');

                int idx = row + col;
                if (idx < span.Length)
                    sb.Append(span[idx].ToString("X2"));
                else
                    sb.Append("  ");  // pad incomplete final row

                if (col < BytesPerRow - 1) sb.Append(' ');
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}

/// <summary>
/// Lazy message formatter. Pretty-prints message type and all properties with indentation.
/// Handles nested message arrays, byte arrays (as hex), and complex object arrays.
/// Only performs reflection and formatting when <see cref="ToString"/> is actually called.
/// Usage: _logger.LogDebug("RX {Message}", new MessageLog(message));
/// </summary>
public readonly struct MessageLog
{
    private readonly IMessageData _message;

    public MessageLog(IMessageData message) => _message = message;

    public override string ToString()
    {
        if (_message is null) return "null";

        var sb = new StringBuilder();
        sb.AppendLine($"[{_message.GetType().Name}]");
        AppendProperties(sb, _message, indentLevel: 1);
        return sb.ToString();
    }

    private static void AppendProperties(StringBuilder sb, object obj, int indentLevel)
    {
        var indent = new string(' ', indentLevel * 5);
        var properties = obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            var value = prop.GetValue(obj);
            var formatted = FormatValue(value, indentLevel);
            sb.Append($"{indent}{prop.Name} = {formatted}");
            if (!formatted.Contains('\n'))
                sb.AppendLine();
        }
    }

    private static string FormatValue(object? value, int indentLevel) => value switch
    {
        null => "null",
        byte[] bytes when bytes.Length > 8 => new HexGrid(bytes).ToString(),
        byte[] bytes => new HexBytes(bytes).ToString(),
        IEnumerable<byte> bytes => new HexBytes(bytes.ToArray()).ToString(),
        string str => $"\"{str}\"",
        string[] strs => FormatStringArray(strs, indentLevel),
        IMessageData[] messages => FormatMessageArray(messages, indentLevel),
        Array array when array.GetType().GetElementType()?.IsEnum == true => FormatEnumArray(array, indentLevel),
        Array array when IsComplexArray(array) => FormatObjectArray(array, indentLevel),
        _ => value.ToString() ?? "null"
    };

    private static string FormatStringArray(string[] strs, int indentLevel)
    {
        if (strs.Length == 0) return "[]";

        var sb = new StringBuilder();
        sb.AppendLine($"[{strs.Length} strings]");

        var indent = new string(' ', (indentLevel + 1) * 5);
        for (int i = 0; i < strs.Length; i++)
            sb.AppendLine($"{indent}[{i}] \"{strs[i]}\"");

        return sb.ToString();
    }

    private static string FormatMessageArray(IMessageData[] messages, int indentLevel)
    {
        if (messages.Length == 0) return "[]";

        var sb = new StringBuilder();
        sb.AppendLine($"[{messages.Length} messages]");

        var indent = new string(' ', (indentLevel + 1) * 5);
        for (int i = 0; i < messages.Length; i++)
        {
            sb.AppendLine($"{indent}[{i}] {messages[i].GetType().Name}");
            AppendProperties(sb, messages[i], indentLevel + 2);
        }
        return sb.ToString();
    }

    private static string FormatEnumArray(Array array, int indentLevel)
    {
        if (array.Length == 0) return "[]";

        var elementType = array.GetType().GetElementType()!;
        bool isFlags = elementType.IsDefined(typeof(FlagsAttribute), false);
        var sb = new StringBuilder();
        sb.AppendLine($"[{array.Length} {elementType.Name}]");

        var indent = new string(' ', (indentLevel + 1) * 5);
        for (int i = 0; i < array.Length; i++)
        {
            var element = array.GetValue(i)!;
            string display = isFlags
                ? $"{element} (0x{Convert.ToUInt64(element):X2})"
                : element.ToString()!;
            sb.AppendLine($"{indent}[{i}] {display}");
        }
        return sb.ToString();
    }

    private static string FormatObjectArray(Array array, int indentLevel)
    {
        if (array.Length == 0) return "[]";

        var elementType = array.GetType().GetElementType()!;
        var sb = new StringBuilder();
        sb.AppendLine($"[{array.Length} {elementType.Name}]");

        var indent = new string(' ', (indentLevel + 1) * 5);
        for (int i = 0; i < array.Length; i++)
        {
            var element = array.GetValue(i);
            if (element is null) { sb.AppendLine($"{indent}[{i}] null"); continue; }

            sb.AppendLine($"{indent}[{i}]");
            AppendProperties(sb, element, indentLevel + 2);
        }
        return sb.ToString();
    }

    private static bool IsComplexArray(Array array)
    {
        var elementType = array.GetType().GetElementType();
        if (elementType is null || elementType == typeof(byte)) return false;
        return Type.GetTypeCode(elementType) == TypeCode.Object && !elementType.IsEnum;
    }
}
