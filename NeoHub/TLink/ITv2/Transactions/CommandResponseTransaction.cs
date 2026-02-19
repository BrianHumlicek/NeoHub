using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.ITv2.Messages;
using Microsoft.Extensions.Logging;

namespace DSC.TLink.ITv2.Transactions
{
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
	internal sealed class CommandResponseTransactionAttribute : TransactionAttribute<CommandResponseTransaction>
	{ }
	/// <summary>
	/// Standard ITv2 command-response transaction pattern.
	/// 
	/// Protocol Flow:
	/// 1. Send Command message
	/// 2. Wait for CommandResponse from remote
	/// 3. Send SimpleAck to acknowledge
	/// 4. Transaction complete
	/// 
	/// This is the most common transaction type in ITv2 protocol.
	/// </summary>
	internal class CommandResponseTransaction : Transaction
	{
		private State _state;
		private CommandResponseCode? _responseCode;

		public CommandResponseTransaction(ILogger log, Func<ITv2MessagePacket, CancellationToken, Task<Result>> sendMessageDelegate, TimeSpan? timeout = null)
			: base(log, sendMessageDelegate, timeout)
		{
			_state = State.Initial;
		}

        protected override bool Pending => _state switch
        {
            State.AwaitingCommandResponse => true,
            State.AwaitingSimpleAck => true,
            _ => false
        };


		protected override async Task InitializeInboundAsync(CancellationToken cancellationToken)
		{
			// Inbound: Remote sent us a command, send CommandResponse back
			var sendResult = await SendMessageAsync(new CommandResponse { ResponseCode = CommandResponseCode.Success }, cancellationToken);
			if (sendResult.IsFailure) return;
			_state = State.AwaitingSimpleAck;
		}

		protected override Task InitializeOutboundAsync(CancellationToken cancellationToken)
		{
			// Outbound: We sent a command, wait for CommandResponse
			_state = State.AwaitingCommandResponse;
			return Task.CompletedTask; // Nothing to send yet
		}

		protected override async Task<bool> TryProcessMessageAsync(ITv2MessagePacket message, CancellationToken cancellationToken)
		{
			switch (_state)
			{
				case State.AwaitingCommandResponse:
					// We sent a command, expecting CommandResponse back
					if (message.messageData is CommandResponse commandResponse)
					{
						if (commandResponse.ResponseCode != CommandResponseCode.Success)
						{
							log.LogWarning("Command rejected with code {Code}", commandResponse.ResponseCode);
							SetResult(new TransactionResult($"CommandResponse error {commandResponse.ResponseCode}"));
							//Maybe I should abort here?
						}
					}
					else if (message.messageData is CommandError commandError)
					{
						log.LogWarning("Command error in transaction {commandError}", commandError.NackCode);
						SetResult(new TransactionResult($"Nack {commandError.NackCode}"));
						//Maybe I should abort here?
					}
					else
					{
                        return HandleUnexpectedResponse(message);
                    }

					// Send SimpleAck to complete transaction
					var sendResult = await SendMessageAsync(new SimpleAck(), cancellationToken);
					if (sendResult.IsFailure) return true;
					_state = State.Complete;
					log.LogDebug("CommandResponse transaction completed");
					SetResult(new TransactionResult());
					break;

				case State.AwaitingSimpleAck:
					// We sent CommandResponse, expecting SimpleAck back
					if (message.messageData is not SimpleAck)
					{
                        return HandleUnexpectedResponse(message);
                    }
					_state = State.Complete;
					SetResult(new TransactionResult(InitiatingMessage));
					log.LogDebug("CommandResponse transaction completed");
					break;

				default:
					throw new InvalidOperationException($"Invalid state {_state} in {nameof(TryProcessMessageAsync)}");
			}
            return true;
        }

        /// <summary>
        /// Get the response code if the transaction completed (for outbound transactions).
        /// </summary>
        public CommandResponseCode? ResponseCode => _responseCode;

		private enum State
		{
			Initial,
			AwaitingSimpleAck,           // Inbound: waiting for remote's ack
			AwaitingCommandResponse,     // Outbound: waiting for remote's response
			Complete
		}
	}
}
