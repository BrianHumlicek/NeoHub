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
using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.ITv2.Messages;
using Microsoft.Extensions.Logging;

namespace DSC.TLink.ITv2.Transactions
{
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
	internal sealed class CommandRequestTransactionAttribute : TransactionAttribute<CommandRequestTransaction>
	{
	}
	/// <summary>
	/// Transaction pattern for CommandRequestMessage.
	///  
	/// Protocol Flow (Outbound only):
	/// 1. Send CommandRequestMessage with CommandRequest enum specifying desired data
	/// 2. Panel responds with typed message matching the requested command
	/// 3. Transaction complete
	/// 
	/// Example: Request zone status → Receive ZoneStatusMessage → Done
	/// </summary>
	internal class CommandRequestTransaction : Transaction
    {
        private State _state;
        private ITv2Command _expectedResponseCommand;
        public CommandRequestTransaction(
            ILogger log,
            Func<ITv2MessagePacket, CancellationToken, Task<Result>> sendMessage,
            TimeSpan? timeout = null)
            : base(log, sendMessage, timeout)
        {
            _state = State.Initial;
        }

        protected override bool Pending => _state switch
        {
            State.AwaitingResponse => true,
            _ => false
        };

        protected override Task InitializeInboundAsync(CancellationToken cancellationToken)
        {
            // Panel doesn't send us CommandRequestMessages - this is outbound only
            log.LogError("CommandRequestTransaction does not support inbound initialization");
            Abort();
            throw new NotSupportedException("CommandRequestTransaction is outbound only - panel does not request data from clients");
        }

        protected override Task InitializeOutboundAsync(CancellationToken cancellationToken)
        {
            if (InitiatingMessage is CommandRequestMessage commandRequest)
            {
                _expectedResponseCommand = commandRequest.CommandRequest;
            }
            else
            {
                log.LogError("CommandRequestTransaction requires CommandRequestMessage as initiating message, but got {MessageType}", InitiatingMessage.GetType().Name);
                Abort();
                throw new InvalidOperationException($"CommandRequestTransaction requires CommandRequestMessage as initiating message, but got {InitiatingMessage.GetType().Name}");
            }
            log.LogTrace("CommandRequest transaction: sent request for {Command}, awaiting response", InitiatingMessage.Command);
            _state = State.AwaitingResponse;
            return Task.CompletedTask;
        }

        protected override async Task<bool> TryProcessMessageAsync(ITv2MessagePacket messagePacket, CancellationToken cancellationToken)
        {
            switch (_state)
            {
                case State.AwaitingResponse:
                    // Validate that the response matches the requested command
                    var responseCommand = messagePacket.messageData.Command;
                    
					if (messagePacket.messageData is CommandError commandError)
					{
						log.LogWarning("Error in CommandRequest {NackCode}", commandError.NackCode);
						SetResult(new TransactionResult($"Command error {commandError.NackCode}"));
						Abort();
						return true;
					}
                    else if (responseCommand != _expectedResponseCommand)
                    {
                        return HandleUnexpectedResponse(messagePacket);
                    }
                    
                    // Transaction complete with the response data
                    log.LogDebug("CommandRequest transaction completed with response type {MessageType}", 
                        messagePacket.messageData.GetType().Name);
                    
                    SetResult(new TransactionResult(messagePacket.messageData));
                    _state = State.Complete;
                    
                    return true;

                default:
                    log.LogWarning("Unexpected message in CommandRequest transaction state {State}", _state);
                    return true;
            }
        }

        private enum State
        {
            Initial,
            AwaitingResponse,
            Complete
        }
    }
}
