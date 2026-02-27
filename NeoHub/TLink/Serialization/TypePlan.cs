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
    internal enum SerializerKind
    {
        Primitive,
        Enum,
        CompactInteger,
        UnicodeString,
        BCDStringFixed,
        BCDStringUnbounded,
        LeadingLengthBCDString,
        ByteArrayFixed,
        ByteArrayLeadingLength,
        ByteArrayUnbounded,
        ObjectArrayLeadingLength,
        DateTime,
        MultipleMessagePacket,
        BitFieldGroupLeader,
        BitFieldGroupMember,
    }

    internal enum DateTimeFormatType
    {
        Date,
        Time,
        DateTime,
    }

    /// <summary>
    /// Pre-computed serialization instructions for a single property.
    /// All reflection and attribute lookups happen once at plan-build time.
    /// </summary>
    internal sealed class PropertyPlan
    {
        public required PropertyInfo Property { get; init; }
        public required SerializerKind Kind { get; init; }
        public TypeCode TypeCode { get; init; }
        public Type? EnumType { get; init; }
        public Type? ElementType { get; init; }
        public int FixedLength { get; init; }
        public int LengthPrefixBytes { get; init; }
        public DateTimeFormatType DateTimeFormat { get; init; }
        public bool IsNullable { get; init; }
        public BitFieldGroupPlan? BitFieldGroup { get; init; }
    }

    internal sealed class BitFieldGroupPlan
    {
        public required BitFieldMemberPlan[] Members { get; init; }
        public required BitFieldStorageSize StorageSize { get; init; }
    }

    internal sealed class BitFieldMemberPlan
    {
        public required PropertyInfo Property { get; init; }
        public required int BitPosition { get; init; }
        public required int BitWidth { get; init; }
        public required bool IsBool { get; init; }
    }

    /// <summary>
    /// Cached serialization plan for a type. Built once, reused for all
    /// subsequent serialization/deserialization of that type.
    /// </summary>
    internal sealed class TypePlan
    {
        public required Func<object> Factory { get; init; }
        public required PropertyPlan[] Properties { get; init; }
    }
}
