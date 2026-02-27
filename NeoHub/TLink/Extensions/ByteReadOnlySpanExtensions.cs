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

using System.Runtime.CompilerServices;

namespace DSC.TLink.Extensions
{
    internal static class ByteReadOnlySpanExtensions
    {
        //byte
        public static byte PopByte(this ref ReadOnlySpan<byte> span, string? message = null)
        {
            byte result = default;
            span.PopAndSetValue((value) => result = value, message);
            return result;
        }

        public static T PopEnum<T>(this ref ReadOnlySpan<byte> span) where T : Enum
        {
            return (T)Enum.ToObject(typeof(T), span.PopByte());
        }
        public static void PopAndSetValue(this ref ReadOnlySpan<byte> span, Action<byte> setterAction, [CallerArgumentExpression(nameof(setterAction))] string? message = null)
        {
            if (!span.TryPopAndSetValue(setterAction)) throw new InvalidOperationException($"Not enough data to read byte: {message}");
        }
        public static bool TryPopAndSetValue(this ref ReadOnlySpan<byte> span, Action<byte> setter)
        {
            if (span.Length < 1) return false;
            setter(span[0]);
            span = span.Slice(1);
            return true;
        }

        //ushort
        public static ushort PopWord(this ref ReadOnlySpan<byte> span, string? message = null)
        {
            ushort result = default;
            span.PopAndSetValue((ushort value) => result = value, message);
            return result;
        }

        public static byte[] PopFixedArray(this ref ReadOnlySpan<byte> span, int Length)
        {
            if (span.Length < Length) throw new InvalidOperationException($"Not enough data to read fixed array of length {Length}; only {span.Length} bytes remain");
            var bytes = span.Slice(0, Length).ToArray();
            span = span.Slice(Length);
            return bytes;
        }
        public static void PopAndSetValue(this ref ReadOnlySpan<byte> span, Action<ushort> setter, [CallerArgumentExpression(nameof(setter))] string? message = null)
        {
            if (!span.TryPopAndSetValue(setter)) throw new InvalidOperationException($"Not enough data to read ushort: {message}");
        }
        public static bool TryPopAndSetValue(this ref ReadOnlySpan<byte> span, Action<ushort> setter)
        {
            if (span.Length < 2) return false;
            setter(BigEndianExtensions.U16(span));
            span = span.Slice(2);
            return true;
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
