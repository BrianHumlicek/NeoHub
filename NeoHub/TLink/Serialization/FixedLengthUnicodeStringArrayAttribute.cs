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
    /// Marks a <c>string[]</c> property as an unbounded array of fixed-length UTF-16BE strings.
    ///
    /// Wire format: [CompactInteger: bytes-per-element][element 0 bytes][element 1 bytes]...
    ///
    /// A single leading CompactInteger encodes the fixed byte width of every element.
    /// All strings are serialized to exactly that many bytes (zero-padded if shorter).
    /// Trailing null characters are stripped on read.
    /// The array has no element count; it reads until the available data is exhausted.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class FixedLengthUnicodeStringArrayAttribute : Attribute { }
}
