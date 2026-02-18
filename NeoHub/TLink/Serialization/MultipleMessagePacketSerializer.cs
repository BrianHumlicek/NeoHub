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
using System.Reflection;

namespace DSC.TLink.Serialization
{
    /// <summary>
    /// Handles serialization of MultipleMessagePacket, which contains an unbounded list
    /// of length-prefixed message payloads. Each sub-message is serialized with MessageFactory
    /// (including its command header) and prefixed with a 2-byte length.
    /// </summary>
    internal class MultipleMessagePacketSerializer : ITypeSerializer
    {
        public bool CanHandle(PropertyInfo property)
        {
            // Only handle IMessageData[] properties that are part of MultipleMessagePacket
            return property.PropertyType == typeof(IMessageData[]) &&
                   property.DeclaringType == typeof(MultipleMessagePacket);
        }

        public void Write(List<byte> bytes, PropertyInfo property, object? value)
        {
            var messages = (IMessageData[]?)value ?? Array.Empty<IMessageData>();

            // Write each message as a length-prefixed payload
            foreach (var message in messages)
            {
                if (message == null)
                    throw new InvalidOperationException("Cannot serialize null message in MultipleMessagePacket");

                // Serialize the message including its command header
                var messageBytes = MessageFactory.SerializeMessage(message);

                // Write 2-byte length prefix
                if (messageBytes.Count > 65535)
                    throw new InvalidOperationException(
                        $"Message payload exceeds maximum length of 65535 bytes (got {messageBytes.Count})");

                PrimitiveSerializer.WriteUInt16(bytes, (ushort)messageBytes.Count);

                // Write the message bytes
                bytes.AddRange(messageBytes.ToArray());
            }
        }

        public object Read(ReadOnlySpan<byte> bytes, ref int offset, PropertyInfo property, int remainingBytes)
        {
            var messages = new List<IMessageData>();

            // Read until we run out of bytes
            while (offset < bytes.Length)
            {
                // Read 1-byte length prefix
                if (offset + 1 > bytes.Length)
                    throw new InvalidOperationException(
                        "Incomplete length prefix in MultipleMessagePacket");

                ushort messageLength = bytes[offset];
                offset += 1;

                // Read the message bytes
                if (offset + messageLength > bytes.Length)
                    throw new InvalidOperationException(
                        $"Incomplete message payload in MultipleMessagePacket (expected {messageLength} bytes, got {bytes.Length - offset})");

                var messageBytes = bytes.Slice(offset, messageLength);
                offset += messageLength;

                // Deserialize using MessageFactory (which reads the command header)
                var message = MessageFactory.DeserializeMessage(messageBytes);
                messages.Add(message);
            }

            return messages.ToArray();
        }
    }
}