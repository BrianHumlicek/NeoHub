using DSC.TLink.ITv2.Enumerations;

namespace DSC.TLink.ITv2.Messages
{
    /// <summary>
    /// Base interface for all ITv2 protocol message data types.
    /// Provides type-safe message handling and automatic serialization via MessageFactory.
    /// </summary>
    public interface IMessageData
    {
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
