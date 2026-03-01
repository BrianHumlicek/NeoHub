using DSC.TLink.ITv2.Enumerations;

namespace DSC.TLink.ITv2.Messages
{
    /// <summary>
    /// Base interface for all ITv2 protocol message data types.
    /// Provides type-safe message handling and automatic serialization via MessageFactory.
    /// </summary>
    public interface IMessageData
    {
        /// <summary>
        /// Serialize this message to bytes for transmission.
        /// Default implementation delegates to MessageFactory.
        /// Override only if custom serialization logic is required.
        /// </summary>
        internal List<byte> Serialize() => MessageFactory.SerializeMessage(this);
        internal ITv2Command Command => MessageFactory.GetCommand(this);
        internal T As<T>() where T : IMessageData
        {
            if (this is T typedMessage)
            {
                return typedMessage;
            }
            throw new InvalidCastException($"Expected message of type {typeof(T).Name} but received {this.GetType().Name}");
        }
    }
}
