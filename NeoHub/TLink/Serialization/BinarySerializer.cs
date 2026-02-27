// DSC TLink - a communications library for DSC Powerseries NEO alarm panels
// Copyright (C) 2024 Brian Humlicek
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Concurrent;
using System.Reflection;
using DSC.TLink.ITv2.Messages;

namespace DSC.TLink.Serialization
{
    /// <summary>
    /// Binary serializer for POCOs using a cached per-type plan.
    /// All reflection and attribute analysis happens once per type at plan-build time.
    /// Subsequent serialize/deserialize calls use only the cached plan with zero reflection overhead.
    /// </summary>
    internal static class BinarySerializer
    {
        private static readonly ConcurrentDictionary<Type, TypePlan> _planCache = new();

        /// <summary>
        /// Serialize a POCO to bytes.
        /// </summary>
        public static List<byte> Serialize(object value)
        {
            var plan = GetOrCreatePlan(value.GetType());
            var bytes = new List<byte>();
            WriteProperties(plan, bytes, value);
            return bytes;
        }

        /// <summary>
        /// Deserialize bytes into an IMessageData instance of the specified type.
        /// </summary>
        public static IMessageData Deserialize(Type type, ReadOnlySpan<byte> bytes)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (!typeof(IMessageData).IsAssignableFrom(type))
                throw new ArgumentException($"Type {type.FullName} must implement IMessageData", nameof(type));

            var (result, _) = DeserializeObject(type, bytes);
            return (IMessageData)result;
        }

        /// <summary>
        /// Generic convenience method for deserializing when the type is known at compile time.
        /// </summary>
        public static T Deserialize<T>(ReadOnlySpan<byte> bytes) where T : class, IMessageData, new()
        {
            return (T)Deserialize(typeof(T), bytes);
        }

        /// <summary>
        /// General-purpose deserialization for any type with a cached plan.
        /// Returns the instance and the number of bytes consumed.
        /// Used by ObjectArraySerializer for element deserialization.
        /// </summary>
        internal static (object instance, int bytesConsumed) DeserializeObject(Type type, ReadOnlySpan<byte> bytes)
        {
            var plan = GetOrCreatePlan(type);
            var result = plan.Factory();
            int offset = 0;
            ReadProperties(plan, bytes, ref offset, result);
            return (result, offset);
        }

        /// <summary>
        /// Clear the plan cache. Useful for testing or if types are dynamically modified.
        /// </summary>
        public static void ClearCache() => _planCache.Clear();

        #region Serialize/Deserialize dispatch

        private static void WriteProperties(TypePlan plan, List<byte> bytes, object instance)
        {
            foreach (var pp in plan.Properties)
            {
                switch (pp.Kind)
                {
                    case SerializerKind.BitFieldGroupMember:
                        break;

                    case SerializerKind.BitFieldGroupLeader:
                        BitFieldSerializer.WriteBitFieldGroup(bytes, pp.BitFieldGroup!, instance);
                        break;

                    case SerializerKind.Primitive:
                        PrimitiveSerializer.WritePrimitive(bytes, pp.TypeCode, pp.Property.GetValue(instance));
                        break;

                    case SerializerKind.Enum:
                        PrimitiveSerializer.WriteEnum(bytes, pp.TypeCode, pp.Property.GetValue(instance));
                        break;

                    case SerializerKind.CompactInteger:
                        CompactIntegerSerializer.Write(bytes, pp.Property.PropertyType, pp.Property.GetValue(instance));
                        break;

                    case SerializerKind.UnicodeString:
                        StringSerializer.WriteUnicodeString(bytes, pp.Property.Name, (string?)pp.Property.GetValue(instance), pp.LengthPrefixBytes);
                        break;

                    case SerializerKind.BCDStringFixed:
                        StringSerializer.WriteBCDStringFixed(bytes, (string?)pp.Property.GetValue(instance), pp.FixedLength);
                        break;

                    case SerializerKind.BCDStringUnbounded:
                        StringSerializer.WriteBCDStringUnbounded(bytes, (string?)pp.Property.GetValue(instance));
                        break;

                    case SerializerKind.LeadingLengthBCDString:
                        StringSerializer.WriteBCDStringPrefixed(bytes, pp.Property.Name, (string?)pp.Property.GetValue(instance));
                        break;

                    case SerializerKind.ByteArrayFixed:
                        ByteArraySerializer.WriteFixedArray(bytes, (byte[]?)pp.Property.GetValue(instance), pp.FixedLength);
                        break;

                    case SerializerKind.ByteArrayLeadingLength:
                        ByteArraySerializer.WriteLeadingLengthArray(bytes, pp.Property.Name, (byte[]?)pp.Property.GetValue(instance), pp.LengthPrefixBytes);
                        break;

                    case SerializerKind.ByteArrayUnbounded:
                        bytes.AddRange((byte[]?)pp.Property.GetValue(instance) ?? Array.Empty<byte>());
                        break;

                    case SerializerKind.ObjectArrayLeadingLength:
                        ObjectArraySerializer.WriteLeadingLength(bytes, pp.Property.Name, pp.Property.GetValue(instance) as Array, pp.LengthPrefixBytes);
                        break;

                    case SerializerKind.DateTime:
                        DateTimeSerializer.Write(bytes, pp.DateTimeFormat, pp.Property.GetValue(instance), pp.IsNullable);
                        break;

                    case SerializerKind.MultipleMessagePacket:
                        MultipleMessagePacketSerializer.Write(bytes, pp.Property.GetValue(instance) as IMessageData[]);
                        break;
                }
            }
        }

        private static void ReadProperties(TypePlan plan, ReadOnlySpan<byte> bytes, ref int offset, object instance)
        {
            foreach (var pp in plan.Properties)
            {
                switch (pp.Kind)
                {
                    case SerializerKind.BitFieldGroupMember:
                        break;

                    case SerializerKind.BitFieldGroupLeader:
                        BitFieldSerializer.ReadBitFieldGroup(bytes, ref offset, pp.BitFieldGroup!, instance);
                        break;

                    case SerializerKind.Primitive:
                        pp.Property.SetValue(instance, PrimitiveSerializer.ReadPrimitive(bytes, ref offset, pp.TypeCode));
                        break;

                    case SerializerKind.Enum:
                        pp.Property.SetValue(instance, PrimitiveSerializer.ReadEnum(bytes, ref offset, pp.EnumType!, pp.TypeCode));
                        break;

                    case SerializerKind.CompactInteger:
                        pp.Property.SetValue(instance, CompactIntegerSerializer.Read(bytes, ref offset, pp.Property.PropertyType));
                        break;

                    case SerializerKind.UnicodeString:
                        pp.Property.SetValue(instance, StringSerializer.ReadUnicodeString(bytes, ref offset, pp.Property.Name, pp.LengthPrefixBytes));
                        break;

                    case SerializerKind.BCDStringFixed:
                        pp.Property.SetValue(instance, StringSerializer.ReadBCDString(bytes, ref offset, pp.Property.Name, pp.FixedLength));
                        break;

                    case SerializerKind.BCDStringUnbounded:
                        pp.Property.SetValue(instance, StringSerializer.ReadBCDString(bytes, ref offset, pp.Property.Name, bytes.Length - offset));
                        break;

                    case SerializerKind.LeadingLengthBCDString:
                    {
                        if (offset >= bytes.Length)
                            throw new InvalidOperationException($"Not enough bytes to read BCD length prefix for '{pp.Property.Name}'");
                        int bcdLen = bytes[offset++];
                        pp.Property.SetValue(instance, StringSerializer.ReadBCDString(bytes, ref offset, pp.Property.Name, bcdLen));
                        break;
                    }

                    case SerializerKind.ByteArrayFixed:
                        pp.Property.SetValue(instance, ByteArraySerializer.ReadFixedArray(bytes, ref offset, pp.Property.Name, pp.FixedLength));
                        break;

                    case SerializerKind.ByteArrayLeadingLength:
                        pp.Property.SetValue(instance, ByteArraySerializer.ReadLeadingLengthArray(bytes, ref offset, pp.Property.Name, pp.LengthPrefixBytes));
                        break;

                    case SerializerKind.ByteArrayUnbounded:
                    {
                        int remaining = bytes.Length - offset;
                        var arr = bytes.Slice(offset, remaining).ToArray();
                        offset += remaining;
                        pp.Property.SetValue(instance, arr);
                        break;
                    }

                    case SerializerKind.ObjectArrayLeadingLength:
                        pp.Property.SetValue(instance, ObjectArraySerializer.ReadLeadingLength(bytes, ref offset, pp.Property.Name, pp.ElementType!, pp.LengthPrefixBytes));
                        break;

                    case SerializerKind.DateTime:
                        pp.Property.SetValue(instance, DateTimeSerializer.Read(bytes, ref offset, pp.DateTimeFormat));
                        break;

                    case SerializerKind.MultipleMessagePacket:
                        pp.Property.SetValue(instance, MultipleMessagePacketSerializer.Read(bytes, ref offset));
                        break;
                }
            }
        }

        #endregion

        #region Plan building (all reflection happens here, once per type)

        private static TypePlan GetOrCreatePlan(Type type)
        {
            return _planCache.GetOrAdd(type, BuildPlan);
        }

        private static TypePlan BuildPlan(Type type)
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && !p.IsDefined(typeof(IgnorePropertyAttribute), false))
                .OrderBy(p => p.MetadataToken)
                .ToArray();

            // Identify bit field groups
            var bitFieldGroups = new Dictionary<string, List<(PropertyInfo prop, BitFieldAttribute attr)>>();
            foreach (var prop in properties)
            {
                var bfAttr = prop.GetCustomAttribute<BitFieldAttribute>();
                if (bfAttr != null)
                {
                    if (!bitFieldGroups.TryGetValue(bfAttr.GroupName, out var list))
                    {
                        list = new();
                        bitFieldGroups[bfAttr.GroupName] = list;
                    }
                    list.Add((prop, bfAttr));
                }
            }

            var assignedGroups = new HashSet<string>();
            var plans = new PropertyPlan[properties.Length];
            for (int i = 0; i < properties.Length; i++)
            {
                plans[i] = BuildPropertyPlan(properties[i], bitFieldGroups, assignedGroups);
            }

            return new TypePlan
            {
                Factory = () => Activator.CreateInstance(type)
                    ?? throw new InvalidOperationException($"Failed to create instance of {type.FullName}. Ensure it has a parameterless constructor."),
                Properties = plans
            };
        }

        private static PropertyPlan BuildPropertyPlan(
            PropertyInfo property,
            Dictionary<string, List<(PropertyInfo prop, BitFieldAttribute attr)>> bitFieldGroups,
            HashSet<string> assignedGroups)
        {
            var propType = property.PropertyType;

            // Bit field
            var bfAttr = property.GetCustomAttribute<BitFieldAttribute>();
            if (bfAttr != null)
            {
                if (assignedGroups.Add(bfAttr.GroupName))
                {
                    var group = bitFieldGroups[bfAttr.GroupName];
                    return new PropertyPlan
                    {
                        Property = property,
                        Kind = SerializerKind.BitFieldGroupLeader,
                        BitFieldGroup = new BitFieldGroupPlan
                        {
                            StorageSize = bfAttr.StorageSize,
                            Members = group.Select(g => new BitFieldMemberPlan
                            {
                                Property = g.prop,
                                BitPosition = g.attr.BitPosition,
                                BitWidth = g.attr.BitWidth,
                                IsBool = g.attr.IsBool,
                            }).ToArray()
                        }
                    };
                }
                return new PropertyPlan
                {
                    Property = property,
                    Kind = SerializerKind.BitFieldGroupMember,
                };
            }

            // MultipleMessagePacket special case
            if (propType == typeof(IMessageData[]) && property.DeclaringType == typeof(MultipleMessagePacket))
            {
                return new PropertyPlan
                {
                    Property = property,
                    Kind = SerializerKind.MultipleMessagePacket,
                };
            }

            // CompactInteger (attribute-driven, check before primitive/enum)
            if (property.IsDefined(typeof(CompactIntegerAttribute), false))
            {
                return new PropertyPlan
                {
                    Property = property,
                    Kind = SerializerKind.CompactInteger,
                    TypeCode = Type.GetTypeCode(propType),
                };
            }

            // String types
            if (propType == typeof(string))
            {
                var unicodeAttr = property.GetCustomAttribute<UnicodeStringAttribute>();
                if (unicodeAttr != null)
                {
                    return new PropertyPlan
                    {
                        Property = property,
                        Kind = SerializerKind.UnicodeString,
                        LengthPrefixBytes = unicodeAttr.LengthBytes,
                    };
                }

                var bcdAttr = property.GetCustomAttribute<BCDStringAttribute>();
                if (bcdAttr != null)
                {
                    return new PropertyPlan
                    {
                        Property = property,
                        Kind = bcdAttr.FixedLength.HasValue ? SerializerKind.BCDStringFixed : SerializerKind.BCDStringUnbounded,
                        FixedLength = bcdAttr.FixedLength ?? 0,
                    };
                }

                if (property.IsDefined(typeof(LeadingLengthBCDStringAttribute), false))
                {
                    return new PropertyPlan
                    {
                        Property = property,
                        Kind = SerializerKind.LeadingLengthBCDString,
                    };
                }

                throw new InvalidOperationException(
                    $"String property '{property.Name}' on type '{property.DeclaringType?.Name}' must have " +
                    "[UnicodeString], [BCDString], or [LeadingLengthBCDString] attribute.");
            }

            // Arrays
            if (propType.IsArray)
            {
                var elementType = propType.GetElementType()!;

                if (elementType == typeof(byte))
                {
                    var fixedAttr = property.GetCustomAttribute<FixedArrayAttribute>();
                    if (fixedAttr != null)
                    {
                        return new PropertyPlan
                        {
                            Property = property,
                            Kind = SerializerKind.ByteArrayFixed,
                            FixedLength = fixedAttr.Length,
                        };
                    }

                    var lengthAttr = property.GetCustomAttribute<LeadingLengthArrayAttribute>();
                    if (lengthAttr != null)
                    {
                        return new PropertyPlan
                        {
                            Property = property,
                            Kind = SerializerKind.ByteArrayLeadingLength,
                            LengthPrefixBytes = lengthAttr.LengthBytes,
                        };
                    }

                    return new PropertyPlan
                    {
                        Property = property,
                        Kind = SerializerKind.ByteArrayUnbounded,
                    };
                }

                if (Type.GetTypeCode(elementType) == TypeCode.Object && !elementType.IsEnum)
                {
                    var lengthAttr = property.GetCustomAttribute<LeadingLengthArrayAttribute>()
                        ?? throw new InvalidOperationException(
                            $"Object array property '{property.Name}' must have [LeadingLengthArray] attribute.");

                    return new PropertyPlan
                    {
                        Property = property,
                        Kind = SerializerKind.ObjectArrayLeadingLength,
                        ElementType = elementType,
                        LengthPrefixBytes = lengthAttr.LengthBytes,
                    };
                }
            }

            // DateTime
            if (propType == typeof(DateTime) || propType == typeof(DateTime?))
            {
                var fmt = DateTimeFormatType.DateTime;
                if (property.IsDefined(typeof(DateFormatAttribute), false))
                    fmt = DateTimeFormatType.Date;
                else if (property.IsDefined(typeof(TimeFormatAttribute), false))
                    fmt = DateTimeFormatType.Time;

                return new PropertyPlan
                {
                    Property = property,
                    Kind = SerializerKind.DateTime,
                    DateTimeFormat = fmt,
                    IsNullable = propType == typeof(DateTime?),
                };
            }

            // Enum
            if (propType.IsEnum)
            {
                var underlyingType = Enum.GetUnderlyingType(propType);
                return new PropertyPlan
                {
                    Property = property,
                    Kind = SerializerKind.Enum,
                    TypeCode = Type.GetTypeCode(underlyingType),
                    EnumType = propType,
                };
            }

            // Primitive
            var tc = Type.GetTypeCode(propType);
            if (tc != TypeCode.Object)
            {
                return new PropertyPlan
                {
                    Property = property,
                    Kind = SerializerKind.Primitive,
                    TypeCode = tc,
                };
            }

            throw new NotSupportedException(
                $"Property '{property.Name}' of type '{propType}' on '{property.DeclaringType?.Name}' is not supported for binary serialization.");
        }

        #endregion
    }

    /// <summary>
    /// Mark properties to exclude from binary serialization (e.g., calculated properties).
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class IgnorePropertyAttribute : Attribute { }
}
