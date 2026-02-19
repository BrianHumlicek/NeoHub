using DSC.TLink.ITv2.Messages;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace DSC.TLink.ITv2.Transactions
{
    static class TransactionFactory
    {
        // Static dictionary mapping message Type -> transaction creator attribute
        private static readonly ImmutableDictionary<Type, ICreateTransaction> _transactionCreators;

        // Static constructor: scan assembly once and populate the lookup
        static TransactionFactory()
        {
            var transactionCreatorsBuilder = ImmutableDictionary.CreateBuilder<Type, ICreateTransaction>();

            var assembly = Assembly.GetExecutingAssembly();

            // Find all types in the assembly that have an attribute implementing ICreateTransaction
            var candidateTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract);

            foreach (var type in candidateTypes)
            {
                // Get all attributes on the type that implement ICreateTransaction
                var creatorAttributes = type.GetCustomAttributes(inherit: false)
                    .OfType<ICreateTransaction>()
                    .ToArray();

                // Register the first matching attribute (or handle multiple if needed)
                if (creatorAttributes.Length > 0)
                {
                    transactionCreatorsBuilder[type] = creatorAttributes[0];
                }
            }
            _transactionCreators = transactionCreatorsBuilder.ToImmutable();
        }

        /// <summary>
        /// Create a transaction for the given message data using the registered attribute factory.
        /// </summary>
        public static Transaction CreateTransaction(IMessageData messageData, ILogger log, Func<ITv2MessagePacket, CancellationToken, Task<Result>> sendMessageDelegate)
        {
            if (messageData == null) throw new ArgumentNullException(nameof(messageData));
            if (log == null) throw new ArgumentNullException(nameof(log));
            if (sendMessageDelegate == null) throw new ArgumentNullException(nameof(sendMessageDelegate));

            var messageType = messageData.GetType();

            if (_transactionCreators.TryGetValue(messageType, out var transactionCreator))
            {
                return transactionCreator.CreateTransaction(log, sendMessageDelegate);
            }

            // Default fallback.  At some point this should probably throw an error but at the time this was written
            //not all messages and transactions were defined, so, this was installed to assist with development.
            log.LogWarning("No transaction attribute found for {MessageType}, using CommandResponseTransaction", messageType.Name);
            return new SimpleAckTransaction(log, sendMessageDelegate);
        }
    }
    internal interface ICreateTransaction
    {
        Transaction CreateTransaction(ILogger log, Func<ITv2MessagePacket, CancellationToken, Task<Result>> sendMessageDelegate);
    }
}