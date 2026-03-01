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
    /// Handles serialization of DateTime values.
    /// 
    /// DateTime Format (32-bit packed):
    /// - Bits 31-27 (5 bits): Hour (0-23)
    /// - Bits 26-21 (6 bits): Minute (0-59)
    /// - Bits 20-15 (6 bits): Second (0-59)
    /// - Bits 14-9  (6 bits): Year - 2000 (0-63, years 2000-2063)
    /// - Bits 8-5   (4 bits): Month (1-12)
    /// - Bits 4-0   (5 bits): Day (1-31)
    /// </summary>
    internal static class DateTimeSerializer
    {
        internal static void Write(List<byte> bytes, DateTimeFormatType format, object? value, bool isNullable)
        {
            var dateTime = value as DateTime?;
            if (!dateTime.HasValue && isNullable)
            {
                WriteNullValue(bytes, format);
                return;
            }

            var dt = dateTime ?? DateTime.MinValue;

            switch (format)
            {
                case DateTimeFormatType.Date:
                    WriteDate(bytes, dt);
                    break;
                case DateTimeFormatType.Time:
                    WriteTime(bytes, dt);
                    break;
                case DateTimeFormatType.DateTime:
                    WriteDateTime(bytes, dt);
                    break;
            }
        }

        internal static object Read(ReadOnlySpan<byte> bytes, ref int offset, DateTimeFormatType format)
        {
            return format switch
            {
                DateTimeFormatType.Date => ReadDate(bytes, ref offset),
                DateTimeFormatType.Time => ReadTime(bytes, ref offset),
                DateTimeFormatType.DateTime => ReadDateTime(bytes, ref offset),
                _ => throw new NotSupportedException($"Unknown DateTime format: {format}")
            };
        }

        private static void WriteDate(List<byte> bytes, DateTime dt)
        {
            // TODO: Implement DSC panel date-only format
            throw new NotImplementedException("Date-only format not yet implemented");
        }

        private static void WriteTime(List<byte> bytes, DateTime dt)
        {
            // TODO: Implement DSC panel time-only format
            throw new NotImplementedException("Time-only format not yet implemented");
        }

        private static void WriteDateTime(List<byte> bytes, DateTime dt)
        {
            // Validate ranges
            if (dt.Year < 2000 || dt.Year > 2063)
                throw new ArgumentOutOfRangeException(nameof(dt), "Year must be between 2000 and 2063 for DSC panel format");

            // Pack into 32-bit value using BitFieldExtensions
            uint packed = 0;
            packed = packed.InsertBits(dt.Hour, 27, 5);           // Bits 31-27: Hour
            packed = packed.InsertBits(dt.Minute, 21, 6);         // Bits 26-21: Minute
            packed = packed.InsertBits(dt.Second, 15, 6);         // Bits 20-15: Second
            packed = packed.InsertBits(dt.Year - 2000, 9, 6);     // Bits 14-9: Year - 2000
            packed = packed.InsertBits(dt.Month, 5, 4);           // Bits 8-5: Month
            packed = packed.InsertBits(dt.Day, 0, 5);             // Bits 4-0: Day

            // Write as big-endian u32
            bytes.Add((byte)(packed >> 24));
            bytes.Add((byte)(packed >> 16));
            bytes.Add((byte)(packed >> 8));
            bytes.Add((byte)packed);
        }

        private static void WriteNullValue(List<byte> bytes, DateTimeFormatType format)
        {
            int byteCount = format switch
            {
                DateTimeFormatType.Date => 4,
                DateTimeFormatType.Time => 4,
                DateTimeFormatType.DateTime => 4,
                _ => 0
            };
            bytes.AddRange(Enumerable.Repeat((byte)0, byteCount));
        }

        private static DateTime ReadDate(ReadOnlySpan<byte> bytes, ref int offset)
        {
            // TODO: Implement DSC panel date-only format parsing
            throw new NotImplementedException("Date-only format not yet implemented");
        }

        private static DateTime ReadTime(ReadOnlySpan<byte> bytes, ref int offset)
        {
            // TODO: Implement DSC panel time-only format parsing
            throw new NotImplementedException("Time-only format not yet implemented");
        }

        private static DateTime ReadDateTime(ReadOnlySpan<byte> bytes, ref int offset)
        {
            // Read big-endian u32
            uint packed = (uint)((bytes[offset] << 24) | (bytes[offset + 1] << 16) |
                                 (bytes[offset + 2] << 8) | bytes[offset + 3]);
            offset += 4;

            // Extract fields using BitFieldExtensions
            int hour = packed.ExtractBits(27, 5);       // Bits 31-27: Hour
            int minute = packed.ExtractBits(21, 6);     // Bits 26-21: Minute
            int second = packed.ExtractBits(15, 6);     // Bits 20-15: Second
            int year = packed.ExtractBits(9, 6) + 2000; // Bits 14-9: Year - 2000
            int month = packed.ExtractBits(5, 4);       // Bits 8-5: Month
            int day = packed.ExtractBits(0, 5);         // Bits 4-0: Day

            // Validate and construct DateTime
            try
            {
                return new DateTime(year, month, day, hour, minute, second);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new InvalidOperationException(
                    $"Invalid DateTime values in packed data: {year:D4}-{month:D2}-{day:D2} {hour:D2}:{minute:D2}:{second:D2}", ex);
            }
        }

        }

    /// <summary>
    /// Specifies that a DateTime property should be serialized as date only.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class DateFormatAttribute : Attribute { }

    /// <summary>
    /// Specifies that a DateTime property should be serialized as time only.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class TimeFormatAttribute : Attribute { }

    /// <summary>
    /// Specifies that a DateTime property should be serialized as full date and time.
    /// Uses 4-byte packed format (32-bit).
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class DateTimeFormatAttribute : Attribute { }
}