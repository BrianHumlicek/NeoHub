// DSC TLink - a communications library for DSC Powerseries NEO alarm panels
// Copyright (C) 2024 Brian Humlicek
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY, without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

namespace DSC.TLink.Extensions
{
    internal static class ByteReadOnlySpanExtensions
    {
        public static byte PopByte(this ref ReadOnlySpan<byte> span)
        {
            if (span.Length < 1) throw new InvalidOperationException("Not enough data to read byte");
            byte result = span[0];
            span = span.Slice(1);
            return result;
        }

        public static ushort PopWord(this ref ReadOnlySpan<byte> span)
        {
            if (span.Length < 2) throw new InvalidOperationException("Not enough data to read ushort");
            ushort result = BigEndianExtensions.U16(span);
            span = span.Slice(2);
            return result;
        }

        public static ushort PopTrailingWord(this ref ReadOnlySpan<byte> span)
        {
            if (span.Length < 2) throw new InvalidOperationException("Not enough data to read trailing word");
            int wordIndex = span.Length - 2;
            ushort result = BigEndianExtensions.U16(span, wordIndex);
            span = span.Slice(0, wordIndex);
            return result;
        }
    }
}
