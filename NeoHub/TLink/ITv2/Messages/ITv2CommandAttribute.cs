using DSC.TLink.ITv2.Enumerations;

namespace DSC.TLink.ITv2.Messages
{
    /// <summary>
    /// Attribute to associate a message type with its ITv2Command enum value.
    /// Applied to message POCOs to enable automatic serialization/deserialization.
    /// App sequence capability is determined by implementing IAppSequenceMessage.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    internal sealed class ITv2CommandAttribute : Attribute
    {
        public ITv2Command Command { get; }

        public ITv2CommandAttribute(ITv2Command command)
        {
            Command = command;
        }
    }
}
