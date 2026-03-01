// DSC TLink - a communications library for DSC Powerseries NEO alarm panels
// Copyright (C) 2024 Brian Humlicek
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Reflection;

namespace DSC.TLink.Serialization
{
    /// <summary>
    /// Handles serialization of properties marked with [BitField] attributes.
    /// Multiple properties with the same group name are packed into a single byte/ushort/uint.
    /// </summary>
    internal static class BitFieldSerializer
    {
        /// <summary>
        /// Write a group of bit field properties that share the same group name.
        /// </summary>
        public static void WriteBitFieldGroup(List<byte> bytes, BitFieldGroupPlan group, object instance)
        {
            uint packedValue = 0;

            foreach (var member in group.Members)
            {
                var value = member.Property.GetValue(instance);

                int fieldValue = member.IsBool
                    ? (bool)value! ? 1 : 0
                    : Convert.ToInt32(value);

                packedValue = packedValue.InsertBits(fieldValue, member.BitPosition, member.BitWidth);
            }

            WritePackedValue(bytes, packedValue, group.StorageSize);
        }

        /// <summary>
        /// Read a group of bit field properties that share the same group name.
        /// </summary>
        public static void ReadBitFieldGroup(ReadOnlySpan<byte> bytes, ref int offset, BitFieldGroupPlan group, object instance)
        {
            uint packedValue = ReadPackedValue(bytes, ref offset, group.StorageSize);

            foreach (var member in group.Members)
            {
                int fieldValue = packedValue.ExtractBits(member.BitPosition, member.BitWidth);

                object value = member.IsBool
                    ? fieldValue != 0
                    : Convert.ChangeType(fieldValue, member.Property.PropertyType);

                member.Property.SetValue(instance, value);
            }
        }

        private static void WritePackedValue(List<byte> bytes, uint value, BitFieldStorageSize size)
        {
            switch (size)
            {
                case BitFieldStorageSize.Byte:
                    bytes.Add((byte)value);
                    break;

                case BitFieldStorageSize.UInt16:
                    bytes.Add((byte)(value >> 8));
                    bytes.Add((byte)(value & 0xFF));
                    break;

                case BitFieldStorageSize.UInt32:
                    bytes.Add((byte)(value >> 24));
                    bytes.Add((byte)(value >> 16));
                    bytes.Add((byte)(value >> 8));
                    bytes.Add((byte)(value & 0xFF));
                    break;
            }
        }

        private static uint ReadPackedValue(ReadOnlySpan<byte> bytes, ref int offset, BitFieldStorageSize size)
        {
            return size switch
            {
                BitFieldStorageSize.Byte => bytes[offset++],

                BitFieldStorageSize.UInt16 => (uint)((bytes[offset++] << 8) | bytes[offset++]),

                BitFieldStorageSize.UInt32 => (uint)((bytes[offset++] << 24) | (bytes[offset++] << 16) |
                                                      (bytes[offset++] << 8) | bytes[offset++]),

                _ => throw new NotSupportedException($"Unsupported storage size: {size}")
            };
        }
    }

    /// <summary>
    /// Marks a property as part of a bit field group.
    /// Multiple properties with the same GroupName are packed into a single byte/ushort/uint.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class BitFieldAttribute : Attribute
    {
        public string GroupName { get; }
        public int BitPosition { get; }
        public int BitWidth { get; }
        public BitFieldStorageSize StorageSize { get; }
        internal bool IsBool { get; }

        /// <summary>
        /// Define a bit field for a boolean property.
        /// </summary>
        /// <param name="groupName">Name of the bit field group (properties with same name are packed together)</param>
        /// <param name="bitPosition">Zero-based bit position within the packed value (0 = LSB)</param>
        /// <param name="storageSize">Total size of the packed value</param>
        public BitFieldAttribute(string groupName, int bitPosition, BitFieldStorageSize storageSize = BitFieldStorageSize.Byte)
        {
            GroupName = groupName;
            BitPosition = bitPosition;
            BitWidth = 1;
            StorageSize = storageSize;
            IsBool = true;
        }

        /// <summary>
        /// Define a bit field for an integer property.
        /// </summary>
        /// <param name="groupName">Name of the bit field group</param>
        /// <param name="bitPosition">Zero-based bit position (0 = LSB)</param>
        /// <param name="bitWidth">Number of bits for this field</param>
        /// <param name="storageSize">Total size of the packed value</param>
        public BitFieldAttribute(string groupName, int bitPosition, int bitWidth, BitFieldStorageSize storageSize = BitFieldStorageSize.Byte)
        {
            GroupName = groupName;
            BitPosition = bitPosition;
            BitWidth = bitWidth;
            StorageSize = storageSize;
            IsBool = false;
        }
    }

    public enum BitFieldStorageSize
    {
        Byte,
        UInt16,
        UInt32
    }
}