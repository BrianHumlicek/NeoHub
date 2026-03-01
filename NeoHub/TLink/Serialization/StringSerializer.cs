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

using System.Text;

namespace DSC.TLink.Serialization
{
    /// <summary>
    /// Handles serialization of string properties with encoding-specific attributes:
    /// - [UnicodeString]: UTF-16LE encoded with a leading length prefix
    /// - [BCDString]: BCD encoded digit string, fixed length or unbounded
    /// - [LeadingLengthBCDString]: BCD encoded digit string with a 1-byte length prefix
    /// </summary>
    internal static class StringSerializer
    {
        internal static void WriteFixedLengthUnicodeStringArray(List<byte> bytes, string[]? strings)
        {
            strings ??= Array.Empty<string>();

            // Determine the fixed element byte width from the widest string
            int fixedLen = 0;
            foreach (var str in strings)
                fixedLen = Math.Max(fixedLen, Encoding.BigEndianUnicode.GetByteCount(str ?? string.Empty));

            // Leading CompactInteger: byte width of each element
            CompactIntegerSerializer.Write(bytes, typeof(int), fixedLen);

            foreach (var str in strings)
            {
                var encoded = Encoding.BigEndianUnicode.GetBytes(str ?? string.Empty);
                bytes.AddRange(encoded);
                for (int i = encoded.Length; i < fixedLen; i++)
                    bytes.Add(0);
            }
        }

        internal static string[] ReadFixedLengthUnicodeStringArrayUnbounded(ReadOnlySpan<byte> bytes, ref int offset)
        {
            int fixedLen = (int)CompactIntegerSerializer.Read(bytes, ref offset, typeof(int));

            var list = new List<string>();
            while (offset < bytes.Length)
            {
                if (offset + fixedLen > bytes.Length)
                    throw new InvalidOperationException(
                        $"Not enough bytes to read fixed-length unicode string (need {fixedLen}, have {bytes.Length - offset})");

                var str = Encoding.BigEndianUnicode.GetString(bytes.Slice(offset, fixedLen));
                offset += fixedLen;
                list.Add(str.TrimEnd('\0'));
            }
            return list.ToArray();
        }

        internal static void WriteUnicodeStringArray(List<byte> bytes, string[]? strings, int lengthBytes)
        {
            foreach (var str in strings ?? Array.Empty<string>())
                WriteUnicodeString(bytes, "[]", str, lengthBytes);
        }

        internal static string[] ReadUnicodeStringArrayUnbounded(ReadOnlySpan<byte> bytes, ref int offset, string propertyName, int lengthBytes)
        {
            var list = new List<string>();
            while (offset < bytes.Length)
                list.Add(ReadUnicodeString(bytes, ref offset, propertyName, lengthBytes));
            return list.ToArray();
        }

        internal static void WriteUnicodeString(List<byte> bytes, string propertyName, string? str, int lengthBytes)
        {
            var encoded = Encoding.Unicode.GetBytes(str ?? string.Empty);

            switch (lengthBytes)
            {
                case 1:
                    if (encoded.Length > 255)
                        throw new InvalidOperationException(
                            $"Property '{propertyName}' encoded string length {encoded.Length} exceeds 1-byte prefix max (255).");
                    bytes.Add((byte)encoded.Length);
                    break;

                case 2:
                    if (encoded.Length > 65535)
                        throw new InvalidOperationException(
                            $"Property '{propertyName}' encoded string length {encoded.Length} exceeds 2-byte prefix max (65535).");
                    bytes.Add((byte)(encoded.Length >> 8));
                    bytes.Add((byte)(encoded.Length & 0xFF));
                    break;

                default:
                    throw new InvalidOperationException($"Invalid length bytes {lengthBytes} for property '{propertyName}'");
            }

            bytes.AddRange(encoded);
        }

        internal static string ReadUnicodeString(ReadOnlySpan<byte> bytes, ref int offset, string propertyName, int lengthBytes)
        {
            int length = lengthBytes switch
            {
                1 => ReadLengthPrefix1(bytes, ref offset, propertyName),
                2 => ReadLengthPrefix2(bytes, ref offset, propertyName),
                _ => throw new InvalidOperationException($"Invalid length prefix size {lengthBytes} for property '{propertyName}'")
            };

            if (offset + length > bytes.Length)
                throw new InvalidOperationException(
                    $"Not enough bytes to read Unicode string '{propertyName}' (need {length}, have {bytes.Length - offset})");

            var str = Encoding.Unicode.GetString(bytes.Slice(offset, length));
            offset += length;
            return str;
        }

        internal static void WriteBCDStringFixed(List<byte> bytes, string? str, int fixedLength)
        {
            var digits = str ?? string.Empty;
            var padded = digits.PadRight(fixedLength * 2, '0');

            for (int i = 0; i < fixedLength; i++)
            {
                byte highNibble = (byte)(padded[i * 2] - '0');
                byte lowNibble = (byte)(padded[i * 2 + 1] - '0');
                bytes.Add((byte)((highNibble << 4) | lowNibble));
            }
        }

        internal static void WriteBCDStringUnbounded(List<byte> bytes, string? str)
        {
            var digits = str ?? string.Empty;
            if (digits.Length % 2 != 0)
                digits += '0';

            int bcdLength = digits.Length / 2;
            WriteBCDStringFixed(bytes, digits, bcdLength);
        }

        internal static void WriteBCDStringPrefixed(List<byte> bytes, string propertyName, string? str)
        {
            var digits = str ?? string.Empty;
            if (digits.Length % 2 != 0)
                digits += '0';

            int bcdLength = digits.Length / 2;
            if (bcdLength > 255)
                throw new InvalidOperationException(
                    $"Property '{propertyName}' BCD byte count {bcdLength} exceeds 1-byte prefix max (255).");

            bytes.Add((byte)bcdLength);
            WriteBCDStringFixed(bytes, digits, bcdLength);
        }

        internal static string ReadBCDString(ReadOnlySpan<byte> bytes, ref int offset, string propertyName, int fixedLength)
        {
            if (offset + fixedLength > bytes.Length)
                throw new InvalidOperationException(
                    $"Not enough bytes to read BCD string '{propertyName}' (need {fixedLength}, have {bytes.Length - offset})");

            var sb = new StringBuilder(fixedLength * 2);
            for (int i = 0; i < fixedLength; i++)
            {
                byte b = bytes[offset++];
                sb.Append((b >> 4) & 0x0F);
                sb.Append(b & 0x0F);
            }

            return sb.ToString().TrimEnd('0');
        }

        private static int ReadLengthPrefix1(ReadOnlySpan<byte> bytes, ref int offset, string propertyName)
        {
            if (offset >= bytes.Length)
                throw new InvalidOperationException($"Not enough bytes to read length prefix for '{propertyName}'");
            return bytes[offset++];
        }

        private static int ReadLengthPrefix2(ReadOnlySpan<byte> bytes, ref int offset, string propertyName)
        {
            if (offset + 1 >= bytes.Length)
                throw new InvalidOperationException($"Not enough bytes to read 2-byte length prefix for '{propertyName}'");
            var length = (bytes[offset] << 8) | bytes[offset + 1];
            offset += 2;
            return length;
        }
    }
}
