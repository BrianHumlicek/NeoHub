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
        private static readonly ImmutableDictionary<ITv2Command, MessageMetadata> _commandLookup;
        private static readonly ImmutableDictionary<Type, MessageMetadata> _typeLookup;

        static MessageFactory()
        {
            var commandLookupBuilder = ImmutableDictionary.CreateBuilder<ITv2Command, MessageMetadata>();
            var typeLookupBuilder = ImmutableDictionary.CreateBuilder<Type, MessageMetadata>();

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
                            $"Types '{commandLookupBuilder[command].messageType.FullName}' and '{type.FullName}' both declare this command.");
                    }

                    var metadata = new MessageMetadata(
                        messageType: type,
                        command: attribute.Command,
                        isPublic: type.IsPublic);

                    commandLookupBuilder[command] = metadata;
                    typeLookupBuilder[type] = metadata;
                }
            }

            _commandLookup = commandLookupBuilder.ToImmutable();
            _typeLookup = typeLookupBuilder.ToImmutable();
        }

        /// <summary>
        /// Deserialize bytes into a strongly-typed message object.
        /// App sequence (if present) is deserialized as a regular property on IAppSequenceMessage types.
        /// </summary>
        public static IMessageData DeserializeMessage(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length == 0)
                return new SimpleAck();
            if (bytes.Length < 2)
                throw new ArgumentException("Message too short to contain command", nameof(bytes));

            // First 2 bytes are the command (ushort)
            var command = (ITv2Command)bytes.PopWord();

            return DeserializeMessage(command, bytes);
        }

        /// <summary>
        /// Deserialize bytes for a known command into a strongly-typed message object.
        /// </summary>
        public static IMessageData DeserializeMessage(ITv2Command command, ReadOnlySpan<byte> payload)
        {
            var messageType = typeof(DefaultMessage);

            if (_commandLookup.TryGetValue(command, out var metadata))
            {
                messageType = metadata.messageType;
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
        /// App sequence (if present) is serialized as a regular property on IAppSequenceMessage types.
        /// </summary>
        public static List<byte> SerializeMessage(IMessageData message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            if (message is SimpleAck)
            {
                return new List<byte>();
            }

            var messageType = message.GetType();

            if (!_typeLookup.TryGetValue(messageType, out var metadata))
            {
                throw new InvalidOperationException(
                    $"No command registered for message type '{messageType.FullName}'. " +
                    $"Ensure the message type is decorated with ITv2CommandAttribute.");
            }

            var result = new List<byte>([
                metadata.command.U16HighByte(),
                metadata.command.U16LowByte()
                ]);

            try
            {
                // Serialize the message payload (includes AppSequence as first property if IAppSequenceMessage)
                result.AddRange(BinarySerializer.Serialize(message));
                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to serialize message type '{messageType.FullName}' for command '{metadata.command}'.", ex);
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

            if (_typeLookup.TryGetValue(messageType, out var metadata))
            {
                return metadata.command;
            }

            throw new InvalidOperationException(
                $"No command registered for message type '{messageType.FullName}'. " +
                $"Ensure the message type is decorated with ITv2CommandAttribute.");
        }

        public static bool IsPublicMessage(ITv2Command command)
        {
            if (_commandLookup.TryGetValue(command, out var metadata))
            {
                return metadata.isPublic;
            }
            return false;
        }

        public static bool CanCreateMessage(ITv2Command command)
        {
            return _commandLookup.ContainsKey(command);
        }

        public static Type? GetMessageType(ITv2Command command)
        {
            if (_commandLookup.TryGetValue(command, out var metadata))
            {
                return metadata.messageType;
            }
            throw new InvalidOperationException(
                $"No message type is registered for command '{command}'. " +
                $"Ensure the there is a message type decorated with ITv2CommandAttribute.");
        }
        record MessageMetadata(Type messageType, ITv2Command command, bool isPublic);
    }
}
