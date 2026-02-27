// DSC TLink - a communications library for DSC Powerseries NEO alarm panels
// Copyright (C) 2024 Brian Humlicek
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace DSC.TLink.Serialization
{
    /// <summary>
    /// Handles serialization of byte arrays with different strategies:
    /// - Fixed-length array (padded/truncated)
    /// - Length-prefixed array
    /// - Unbounded array (consumes all remaining bytes, must be last property)
    /// </summary>
    internal static class ByteArraySerializer
    {
        internal static void WriteFixedArray(List<byte> bytes, byte[]? value, int fixedLength)
        {
            var arr = value ?? Array.Empty<byte>();
            if (arr.Length >= fixedLength)
            {
                bytes.AddRange(arr.AsSpan(0, fixedLength));
            }
            else
            {
                bytes.AddRange(arr);
                bytes.AddRange(Enumerable.Repeat((byte)0, fixedLength - arr.Length));
            }
        }

        internal static void WriteLeadingLengthArray(List<byte> bytes, string propertyName, byte[]? value, int lengthBytes)
        {
            var arr = value ?? Array.Empty<byte>();
            switch (lengthBytes)
            {
                case 1:
                    if (arr.Length > 255)
                        throw new InvalidOperationException(
                            $"Property '{propertyName}' array length {arr.Length} exceeds 1-byte prefix max (255).");
                    bytes.Add((byte)arr.Length);
                    break;

                case 2:
                    if (arr.Length > 65535)
                        throw new InvalidOperationException(
                            $"Property '{propertyName}' array length {arr.Length} exceeds 2-byte prefix max (65535).");
                    bytes.Add((byte)(arr.Length >> 8));
                    bytes.Add((byte)(arr.Length & 0xFF));
                    break;

                default:
                    throw new InvalidOperationException($"Invalid length bytes {lengthBytes} for property '{propertyName}'");
            }
            bytes.AddRange(arr);
        }

        internal static byte[] ReadFixedArray(ReadOnlySpan<byte> bytes, ref int offset, string propertyName, int fixedLength)
        {
            if (offset + fixedLength > bytes.Length)
                throw new InvalidOperationException(
                    $"Not enough bytes to read fixed array '{propertyName}' (need {fixedLength}, have {bytes.Length - offset})");

            var arr = bytes.Slice(offset, fixedLength).ToArray();
            offset += fixedLength;
            return arr;
        }

        internal static byte[] ReadLeadingLengthArray(ReadOnlySpan<byte> bytes, ref int offset, string propertyName, int lengthBytes)
        {
            int length = lengthBytes switch
            {
                1 => ReadLengthPrefix1(bytes, ref offset, propertyName),
                2 => ReadLengthPrefix2(bytes, ref offset, propertyName),
                _ => throw new InvalidOperationException($"Invalid length prefix size {lengthBytes} for property '{propertyName}'")
            };

            if (offset + length > bytes.Length)
                throw new InvalidOperationException(
                    $"Not enough bytes to read variable array '{propertyName}' (need {length}, have {bytes.Length - offset})");

            var arr = bytes.Slice(offset, length).ToArray();
            offset += length;
            return arr;
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