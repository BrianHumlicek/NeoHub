using DSC.TLink.Extensions;
using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.Serialization;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace DSC.TLink.ITv2.Messages
{
    internal static class MessageFactory
    {
        private static readonly ImmutableDictionary<ITv2Command, Type> _commandLookup;
        private static readonly ImmutableDictionary<Type, ITv2Command> _typeLookup;

        static MessageFactory()
        {
            var commandLookupBuilder = ImmutableDictionary.CreateBuilder<ITv2Command, Type>();
            var typeLookupBuilder = ImmutableDictionary.CreateBuilder<Type, ITv2Command>();

            var assembly = Assembly.GetExecutingAssembly();
            var messageDataTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(IMessageData).IsAssignableFrom(t));

            foreach (var type in messageDataTypes)
            {
                var attribute = type.GetCustomAttribute<ITv2CommandAttribute>(inherit: false);
                if (attribute != null)
                {
                    var command = attribute.Command;

                    if (commandLookupBuilder.ContainsKey(command))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate ITv2CommandAttribute found for command '{command}'. " +
                            $"Types '{commandLookupBuilder[command].FullName}' and '{type.FullName}' both declare this command.");
                    }


                    commandLookupBuilder[command] = type;
                    typeLookupBuilder[type] = command;
                }
            }

            _commandLookup = commandLookupBuilder.ToImmutable();
            _typeLookup = typeLookupBuilder.ToImmutable();
        }

        /// <summary>
        /// Deserialize bytes into a strongly-typed message object.
        /// The BinarySerializer automatically handles CommandSequence for command messages.
        /// </summary>
        public static IMessageData DeserializeMessage(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length == 0)
                return new SimpleAck();
            if (bytes.Length < 2)
                throw new ArgumentException("Message too short to contain command", nameof(bytes));

            // First 2 bytes are the command (ushort)
            var command = (ITv2Command)bytes.PopWord();

            // Deserialize the message payload (BinarySerializer handles CommandSequence automatically)
            return DeserializeMessage(command, bytes);
        }

        /// <summary>
        /// Deserialize bytes for a known command into a strongly-typed message object.
        /// </summary>
        public static IMessageData DeserializeMessage(ITv2Command command, ReadOnlySpan<byte> payload)
        {
            var messageType = typeof(DefaultMessage);
            
            if (_commandLookup.TryGetValue(command, out var type))
            {
                messageType = type;
            }

            try
            {
                var message = BinarySerializer.Deserialize(messageType, payload);
                if (message is not IMessageData typedMessage)
                {
                    throw new InvalidOperationException(
                        $"Deserialized message type '{messageType.FullName}' does not implement IMessageData.");
                }
                else if (message is DefaultMessage defaultMessage)
                {
                    defaultMessage.Command = command;
                }
                return typedMessage;
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"Failed to deserialize message for command '{command}' into type '{messageType.FullName}'.", ex);
            }
        }

        /// <summary>
        /// Serialize a message object to bytes including the command header.
        /// The BinarySerializer automatically handles CommandSequence for command messages.
        /// </summary>
        public static List<byte> SerializeMessage(IMessageData message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            if (message is SimpleAck)
            {
                return new List<byte>();
            }

            var messageType = message.GetType();

            if (!_typeLookup.TryGetValue(messageType, out ITv2Command command))
            {
                throw new InvalidOperationException(
                    $"No command registered for message type '{messageType.FullName}'. " +
                    $"Ensure the message type is decorated with ITv2CommandAttribute.");
            }

            var result = new List<byte>([
                command.U16HighByte(),
                command.U16LowByte()
                ]);

            try
            {
                // Serialize the message payload (BinarySerializer handles CommandSequence automatically)
                result.AddRange(BinarySerializer.Serialize(message));
                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to serialize message type '{messageType.FullName}' for command '{command}'.", ex);
            }
        }

        /// <summary>
        /// Serialize just the message payload without the command header.
        /// Used when the command is already in the protocol frame.
        /// </summary>
        public static List<byte> SerializeMessagePayload(IMessageData message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            var messageType = message.GetType();
            return BinarySerializer.Serialize(message);
        }

        public static ITv2Command GetCommand(IMessageData message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            var messageType = message.GetType();

            if (_typeLookup.TryGetValue(messageType, out var command))
            {
                return command;
            }

            throw new InvalidOperationException(
                $"No command registered for message type '{messageType.FullName}'. " +
                $"Ensure the message type is decorated with ITv2CommandAttribute.");
        }
    }
}
