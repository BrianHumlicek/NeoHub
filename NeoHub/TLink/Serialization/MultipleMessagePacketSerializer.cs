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

using DSC.TLink.ITv2.Messages;

namespace DSC.TLink.Serialization
{
    /// <summary>
    /// Handles serialization of MultipleMessagePacket, which contains an unbounded list
    /// of length-prefixed message payloads. Each sub-message is serialized with MessageFactory
    /// (including its command header) and prefixed with a variable-length size
    /// (same encoding as ITv2 framing: 1 byte if &lt; 0x80, otherwise 2 bytes with bit 7 set on the first).
    /// </summary>
    internal static class MultipleMessagePacketSerializer
    {
        internal static void Write(List<byte> bytes, IMessageData[]? messages)
        {
            foreach (var message in messages ?? Array.Empty<IMessageData>())
            {
                if (message == null)
                    throw new InvalidOperationException("Cannot serialize null message in MultipleMessagePacket");

                var messageBytes = MessageFactory.SerializeMessage(message);

                if (messageBytes.Count > 0x7FFF)
                    throw new InvalidOperationException(
                        $"Message payload exceeds maximum length of {0x7FFF} bytes (got {messageBytes.Count})");

                WriteLength(bytes, messageBytes.Count);
                bytes.AddRange(messageBytes.ToArray());
            }
        }

        internal static IMessageData[] Read(ReadOnlySpan<byte> bytes, ref int offset)
        {
            var messages = new List<IMessageData>();

            while (offset < bytes.Length)
            {
                int messageLength = ReadLength(bytes, ref offset);

                if (offset + messageLength > bytes.Length)
                    throw new InvalidOperationException(
                        $"Incomplete message payload in MultipleMessagePacket (expected {messageLength} bytes, got {bytes.Length - offset})");

                var messageBytes = bytes.Slice(offset, messageLength);
                offset += messageLength;

                var message = MessageFactory.DeserializeMessage(messageBytes);
                messages.Add(message);
            }

            return messages.ToArray();
        }

        // Variable-length prefix: same encoding as ITv2Framing (1 byte if < 0x80, else 2 bytes with bit 7 set).
        private static void WriteLength(List<byte> bytes, int length)
        {
            if (length > 0x7F)
            {
                bytes.Add((byte)((length >> 8) | 0x80));
                bytes.Add((byte)(length & 0xFF));
            }
            else
            {
                bytes.Add((byte)length);
            }
        }

        private static int ReadLength(ReadOnlySpan<byte> bytes, ref int offset)
        {
            if (offset >= bytes.Length)
                throw new InvalidOperationException("Incomplete length prefix in MultipleMessagePacket");

            byte first = bytes[offset++];
            if ((first & 0x80) != 0)
            {
                if (offset >= bytes.Length)
                    throw new InvalidOperationException("Incomplete 2-byte length prefix in MultipleMessagePacket");
                byte second = bytes[offset++];
                return ((first & 0x7F) << 8) | second;
            }
            return first;
        }
    }
}