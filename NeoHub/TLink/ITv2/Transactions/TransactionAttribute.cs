using Microsoft.Extensions.Logging;

namespace DSC.TLink.ITv2.Transactions
{
	internal abstract class TransactionAttribute<T> : Attribute, ICreateTransaction where T : Transaction
	{
		/// <summary>
		/// Optional timeout for the transaction. If null, uses default timeout.
		/// </summary>
		public TimeSpan? Timeout { get; init; }
		public Transaction CreateTransaction(ILogger log, Func<ITv2MessagePacket, CancellationToken, Task<Result>> sendMessageDelegate)
		{
			var result = Activator.CreateInstance(typeof(T), log, sendMessageDelegate, Timeout);
			return (T)(result ?? throw new InvalidOperationException($"Error creating transaction of type {typeof(T)}"));
		}
	}
}
