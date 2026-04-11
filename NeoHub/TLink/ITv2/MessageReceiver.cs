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

using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.ITv2.Messages;

namespace DSC.TLink.ITv2;

internal interface IMessageReceiver : IDisposable
{
    bool TryReceive(ITv2Packet packet);
    bool TryReceiveSubMessage(IMessageData message);
    Task<IMessageData?> Result(CancellationToken ct);
    bool IsCompleted { get; }
}

/// <summary>
/// Tracks a pending outbound message waiting for a response.
///
/// For notifications: completes when a SimpleAck arrives with matching ReceiverSequence.
/// For commands: completes when a command message arrives with matching CommandSequence
/// (SimpleAck just marks protocol-level acknowledgement for async responses).
/// </summary>
internal sealed class MessageReceiver : IMessageReceiver
{
    private readonly byte _senderSequence;
    private readonly byte? _commandSequence;
    private readonly ITv2Command? _senderCommandType;
    private readonly ITv2Command? _receiveCommandType;
    private readonly TaskCompletionSource<IMessageData?> _tcs;

    private MessageReceiver(byte senderSequence, byte? commandSequence, ITv2Command? command)
    {
        _senderSequence = senderSequence;
        _commandSequence = commandSequence;
        if (command.HasValue)
        {
            _senderCommandType = command.Value;
            _receiveCommandType = (ITv2Command)((int)command | 0x4000);
        }
        _tcs = new TaskCompletionSource<IMessageData?>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public static MessageReceiver CreateNotificationReceiver(byte senderSequence)
        => new(senderSequence, commandSequence: null, command: null);

    public static MessageReceiver CreateCommandReceiver(byte senderSequence, byte commandSequence, ITv2Command command)
        => new(senderSequence, commandSequence, command);

    public bool TryReceive(ITv2Packet packet)
    {
        if (packet.Message is CommandError globalError && globalError.NackCommand == _senderCommandType)
        {
            _tcs.TrySetResult(packet.Message);
            return true;
        }

        // Some panel commands ACK on the original transaction and then deliver
        // the actual response as a new inbound transaction (different receiver sequence).
        // Accept command-type response globally by command id.
        if (_commandSequence is not null && packet.Message is not SimpleAck && packet.Message.Command == _receiveCommandType)
        {
            _tcs.TrySetResult(packet.Message);
            return true;
        }

        if (packet.ReceiverSequence == _senderSequence)
        {
            if (packet.Message is CommandError errorMessage && errorMessage.NackCommand == _senderCommandType)
            {
                _tcs.TrySetResult(packet.Message);
                return true;
            }
            //This appears to be a sequencing strategy in addition to command sequence.
            //Message responses have have bit18 set in the command byte to indicate it is a response
            if (packet.Message is not SimpleAck && packet.Message.Command == _receiveCommandType)
            {
                _tcs.TrySetResult(packet.Message);
                return true;
            }
            //SimpleAck is sufficient to complete a non-command sequence transaction eg: a notification
            if (packet.Message is SimpleAck && _commandSequence is null)
            {
                _tcs.TrySetResult(null);
                return true;
            }
            // Protocol-level ack for a command; consume without completing
            if (packet.Message is SimpleAck && _commandSequence is not null)
            {
                return true;
            }
        }

        if (packet.Message is ICommandMessage commandMessage)
            return TryReceiveSubMessage(commandMessage);

        return false;
    }

    public bool TryReceiveSubMessage(IMessageData message)
    {
        if (_commandSequence is not null)
        {
            if (message is CommandError error && error.NackCommand == _senderCommandType)
            {
                _tcs.TrySetResult(error);
                return true;
            }

            // Async command responses can arrive as non-ICommandMessage payloads
            // embedded inside MultipleMessagePacket. Match by response command id.
            if (message.Command == _receiveCommandType)
            {
                _tcs.TrySetResult(message);
                return true;
            }
        }

        if (_commandSequence is not null && message is ICommandMessage cmd && cmd.CommandSequence == _commandSequence)
        {
            _tcs.TrySetResult(cmd);
            return true;
        }
        return false;
    }

    public Task<IMessageData?> Result(CancellationToken ct)
    {
        ct.Register(() => _tcs.TrySetCanceled(ct));
        return _tcs.Task;
    }

    public bool IsCompleted => _tcs.Task.IsCompleted;

    public void Dispose() => _tcs.TrySetCanceled();
}
