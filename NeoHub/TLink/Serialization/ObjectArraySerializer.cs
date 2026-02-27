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

namespace DSC.TLink.Serialization
{
    /// <summary>
    /// Handles serialization of arrays of complex objects (records/classes).
    /// Delegates element serialization to BinarySerializer, eliminating duplicated logic.
    /// </summary>
    internal static class ObjectArraySerializer
    {
        internal static void WriteLeadingLength(List<byte> bytes, string propertyName, Array? arr, int lengthPrefixBytes)
        {
            arr ??= Array.Empty<object>();
            WriteLengthPrefix(bytes, propertyName, arr.Length, lengthPrefixBytes);

            foreach (var element in arr)
            {
                if (element == null)
                    throw new InvalidOperationException("Cannot serialize null elements in object array");

                bytes.AddRange(BinarySerializer.Serialize(element));
            }
        }

        internal static Array ReadLeadingLength(ReadOnlySpan<byte> bytes, ref int offset, string propertyName, Type elementType, int lengthPrefixBytes)
        {
            int count = ReadLengthPrefix(bytes, ref offset, propertyName, lengthPrefixBytes);
            var arr = Array.CreateInstance(elementType, count);

            for (int i = 0; i < count; i++)
            {
                var elementSpan = bytes.Slice(offset);
                var (element, consumed) = BinarySerializer.DeserializeObject(elementType, elementSpan);
                arr.SetValue(element, i);
                offset += consumed;
            }

            return arr;
        }

        private static void WriteLengthPrefix(List<byte> bytes, string propertyName, int length, int lengthPrefixBytes)
        {
            switch (lengthPrefixBytes)
            {
                case 1:
                    if (length > 255)
                        throw new InvalidOperationException(
                            $"Property '{propertyName}' array length {length} exceeds 1-byte prefix max (255).");
                    bytes.Add((byte)length);
                    break;

                case 2:
                    if (length > 65535)
                        throw new InvalidOperationException(
                            $"Property '{propertyName}' array length {length} exceeds 2-byte prefix max (65535).");
                    PrimitiveSerializer.WriteUInt16(bytes, (ushort)length);
                    break;

                default:
                    throw new InvalidOperationException($"Invalid length bytes {lengthPrefixBytes} for property '{propertyName}'");
            }
        }

        private static int ReadLengthPrefix(ReadOnlySpan<byte> bytes, ref int offset, string propertyName, int lengthPrefixBytes)
        {
            return lengthPrefixBytes switch
            {
                1 when offset < bytes.Length => bytes[offset++],
                2 when offset + 1 < bytes.Length => PrimitiveSerializer.ReadUInt16(bytes, ref offset),
                1 or 2 => throw new InvalidOperationException(
                    $"Not enough bytes to read length prefix for '{propertyName}'"),
                _ => throw new InvalidOperationException(
                    $"Invalid length prefix size {lengthPrefixBytes} for property '{propertyName}'")
            };
        }
    }
}
