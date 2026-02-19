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

using DSC.TLink.Extensions;
using DSC.TLink.ITv2.Messages;
using Microsoft.Extensions.Logging;

namespace DSC.TLink.ITv2.Transactions
{
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
	internal sealed class SimpleAckTransactionAttribute : TransactionAttribute<SimpleAckTransaction>
	{
	}
	/// <summary>
	/// Simple ITv2 transaction pattern with just message + ack.
	/// 
	/// Protocol Flow:
	/// Inbound:
	/// 1. Receive data message from remote
	/// 2. Send SimpleAck to acknowledge
	/// 3. Transaction complete
	/// 
	/// Outbound:
	/// 1. Send data message to remote
	/// 2. Wait for SimpleAck from remote
	/// 3. Transaction complete
	/// 
	/// This is used for broadcasts and notifications that don't need a CommandResponse.
	/// </summary>
	internal class SimpleAckTransaction : Transaction
	{
		private State _state;

		public SimpleAckTransaction(ILogger log, Func<ITv2MessagePacket, CancellationToken, Task<Result>> sendMessageDelegate, TimeSpan? timeout = null)
			: base(log, sendMessageDelegate, timeout)
		{
			_state = State.Initial;
		}

		protected override bool Pending => _state switch
		{
			State.AwaitingAck => true,
			_ => false
		};

		protected override async Task InitializeInboundAsync(CancellationToken cancellationToken)
		{
			// Inbound: Remote sent us a message, send SimpleAck back
			log.LogTrace("SimpleAck transaction: received message, sending ack");
			_state = State.SendingSimpleAck;
			var sendResult = await SendMessageAsync(new SimpleAck(), cancellationToken);
			if (sendResult.IsFailure) return;
			_state = State.Complete;
			SetResult(new TransactionResult(InitiatingMessage));
			log.LogDebug("SimpleAck transaction completed (inbound)");
		}

		protected override async Task InitializeOutboundAsync(CancellationToken cancellationToken)
		{
			// Outbound: We sent a message, wait for SimpleAck
			log.LogTrace("SimpleAck transaction: sent message, awaiting ack");
			_state = State.AwaitingAck;
			await Task.CompletedTask; // Nothing more to send yet
		}

		protected override async Task<bool> TryProcessMessageAsync(ITv2MessagePacket message, CancellationToken cancellationToken)
		{
			switch (_state)
			{
				case State.AwaitingAck:
					// We sent a message, expecting Ack back
					if (message.messageData is SimpleAck)
					{
                        _state = State.Complete;
                        log.LogDebug("SimpleAck transaction completed (outbound)");
                        SetResult(new TransactionResult());
                        break;
					}
                    else if (message.messageData is CommandError errorMessage)
                    {
                        _state = State.Complete;
                        log.LogWarning("Received CommandError NAck: Command={Command}, NackCode={NackCode}",
                            errorMessage.Command, errorMessage.NackCode);
                        SetResult(new TransactionResult(errorMessage.NackCode.ToString()));
                        break;
                    }
                    return HandleUnexpectedResponse(message);
                default:
					log.LogWarning("Unexpected message in state {State}", _state);
					break;
			}

			return true;
		}

		private enum State
		{
			Initial,
			SendingSimpleAck,     // Inbound: sending our ack
			AwaitingAck,    // Outbound: waiting for remote's ack
			Complete
		}
	}
}