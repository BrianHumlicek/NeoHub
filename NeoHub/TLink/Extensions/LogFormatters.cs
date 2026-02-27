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
/// Lazy formatting structs for logging. These are value types that capture a reference
/// to the data but defer all formatting work to ToString(), which the logging pipeline
/// only calls after confirming the log level is enabled. This means zero cost when
/// the log level is off — no string allocations, no hex conversions, no reflection.
/// 
/// Usage:
///   _logger.LogTrace("Sent {Bytes}", new HexBytes(bytes));
///   _logger.LogDebug("Received {Message}", new MessageLog(message));
/// </summary>
public static class LogFormatters
{
    /// <summary>
    /// Lazy hex formatter for byte data. Formats as [XX-XX-XX-...] only when ToString() is called.
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

            // Pre-allocate: each byte is "XX" (2 chars) + separator "-" (1 char), minus last separator, plus brackets
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
    /// Lazy message formatter. Pretty-prints message type and all properties with indentation.
    /// Handles nested message arrays, byte arrays (as hex), and complex object arrays.
    /// Only performs reflection and formatting when ToString() is actually called.
    /// </summary>
    public readonly struct MessageLog
    {
        private readonly IMessageData _message;

        public MessageLog(IMessageData message) => _message = message;

        public override string ToString()
        {
            if (_message is null) return "null";

            var sb = new StringBuilder();
            var type = _message.GetType();

            sb.AppendLine($"[{type.Name}]");
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
            byte[] bytes => new HexBytes(bytes).ToString(),
            IEnumerable<byte> bytes => new HexBytes(bytes.ToArray()).ToString(),
            string str => $"\"{str}\"",
            IMessageData[] messages => FormatMessageArray(messages, indentLevel),
            Array array when IsComplexArray(array) => FormatObjectArray(array, indentLevel),
            _ => value.ToString() ?? "null"
        };

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
}
