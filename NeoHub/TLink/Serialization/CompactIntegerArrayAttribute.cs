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
    /// Marks a byte array property as having a CompactInteger (VarBytes) length prefix.
    /// The serialized format is: [CompactInt: length][data bytes...]
    ///
    /// This differs from <see cref="LeadingLengthArrayAttribute"/> which uses a fixed 1- or 2-byte
    /// length prefix. CompactInteger encoding stores only the significant bytes of the length value,
    /// e.g. length=4 encodes as [0x01, 0x04] (2 bytes) rather than [0x04] (1 byte).
    ///
    /// Used by 0x0704 ConfigurationEnter for the access code field.
    /// </summary>
    /// <example>
    /// [CompactIntegerArray]
    /// public byte[] AccessCode { get; init; } = Array.Empty&lt;byte&gt;();
    /// </example>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class CompactIntegerArrayAttribute : Attribute
    {
    }
}
