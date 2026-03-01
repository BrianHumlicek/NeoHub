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
    /// Handles serialization of integers with [CompactInteger] attribute.
    /// Only stores significant bytes with a leading length prefix.
    /// </summary>
    internal static class CompactIntegerSerializer
    {
        internal static void Write(List<byte> bytes, Type propertyType, object? value)
        {
            byte[] fullBytes = PrimitiveSerializer.GetBytes(value ?? GetDefaultValue(propertyType), propertyType);
            int startIndex = FindSignificantByteIndex(fullBytes, propertyType);
            int length = fullBytes.Length - startIndex;

            bytes.Add((byte)length);
            bytes.AddRange(fullBytes.AsSpan(startIndex));
        }

        internal static object Read(ReadOnlySpan<byte> bytes, ref int offset, Type propertyType)
        {
            if (offset >= bytes.Length)
                throw new InvalidOperationException(
                    "Not enough bytes to read length prefix for compact integer");

            int length = bytes[offset++];

            if (offset + length > bytes.Length)
                throw new InvalidOperationException(
                    $"Not enough bytes to read compact integer (expected {length}, got {bytes.Length - offset})");

            var compactBytes = bytes.Slice(offset, length);
            offset += length;

            bool isSigned = IsSigned(propertyType);
            return PrimitiveSerializer.ReadFromBytes(compactBytes, propertyType, signExtend: isSigned);
        }

        private static int FindSignificantByteIndex(byte[] fullBytes, Type type)
        {
            int startIndex = 0;
            bool isSigned = IsSigned(type);

            if (isSigned)
            {
                // For signed types, keep the byte with the sign bit
                bool isNegative = (fullBytes[0] & 0x80) != 0;
                
                for (int i = 0; i < fullBytes.Length - 1; i++)
                {
                    byte currentByte = fullBytes[i];
                    byte nextByte = fullBytes[i + 1];
                    
                    if (isNegative)
                    {
                        if (currentByte == 0xFF && (nextByte & 0x80) != 0)
                            startIndex = i + 1;
                        else
                            break;
                    }
                    else
                    {
                        if (currentByte == 0x00 && (nextByte & 0x80) == 0)
                            startIndex = i + 1;
                        else
                            break;
                    }
                }
            }
            else
            {
                // For unsigned types, skip leading zeros
                while (startIndex < fullBytes.Length - 1 && fullBytes[startIndex] == 0)
                {
                    startIndex++;
                }
            }

            return startIndex;
        }

        private static bool IsSigned(Type type)
        {
            return Type.GetTypeCode(type) is 
                TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64;
        }

        private static object GetDefaultValue(Type type)
        {
            return Type.GetTypeCode(type) switch
            {
                TypeCode.Byte => (byte)0,
                TypeCode.SByte => (sbyte)0,
                TypeCode.UInt16 => (ushort)0,
                TypeCode.Int16 => (short)0,
                TypeCode.UInt32 => (uint)0,
                TypeCode.Int32 => 0,
                TypeCode.UInt64 => (ulong)0,
                TypeCode.Int64 => (long)0,
                _ => throw new NotSupportedException()
            };
        }

        // Remove all the duplicate GetBytes* and Read* methods - use PrimitiveSerializer instead
    }
}