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

/// <summary>
/// Handles the response pattern for <see cref="CommandRequestMessage"/>:
/// the panel delivers the requested data as a new inbound transaction rather than a direct
/// command response. An optional ack (CommandResponse or SimpleAck) may arrive first.
///
/// Completes as soon as the expected notification type is received, regardless of whether
/// a protocol ack preceded it. If an ack does arrive, it is consumed silently. The response
/// notification is both published to the notification channel AND returned as the SendAsync result.
/// Unknown message types arrive as <see cref="DefaultMessage"/> and are matched by command word.
/// </summary>
internal sealed class CommandRequestReceiver : IMessageReceiver
{
    private readonly byte _senderSequence;
    private readonly byte _commandSequence;
    private readonly Type? _expectedType;
    private readonly ITv2Command _expectedCommand;
    private readonly Action<IMessageData> _publish;
    private readonly TaskCompletionSource<IMessageData?> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _acknowledged;

    public CommandRequestReceiver(byte senderSequence, byte commandSequence, ITv2Command expectedCommand, Type? expectedType, Action<IMessageData> publish)
    {
        _senderSequence = senderSequence;
        _commandSequence = commandSequence;
        _expectedCommand = expectedCommand;
        _expectedType = expectedType;
        _publish = publish;
    }

    public bool TryReceive(ITv2Packet packet)
    {
        if (IsExpected(packet.Message))
        {
            Complete(packet.Message);
            return true;
        }

        if (!_acknowledged)
        {
            if (packet.ReceiverSequence == _senderSequence && packet.Message is SimpleAck)
            {
                _acknowledged = true;
                return true;
            }
            if (packet.Message is ICommandMessage cmd && cmd.CommandSequence == _commandSequence)
            {
                Complete(packet.Message);
                return true;
            }
        }

        return false;
    }

    public bool TryReceiveSubMessage(IMessageData message)
    {
        if (IsExpected(message))
        {
            Complete(message);
            return true;
        }

        if (!_acknowledged && message is ICommandMessage cmd && cmd.CommandSequence == _commandSequence)
        {
            if (cmd is CommandResponse { ResponseCode: not CommandResponseCode.Success } errorResponse)
            {
                Complete(errorResponse);
                return true;
            }
            _acknowledged = true;
            return true;
        }

        return false;
    }

    private bool IsExpected(IMessageData message) =>
        (_expectedType is not null && message.GetType() == _expectedType) ||
        (message is DefaultMessage dm && dm.Command == _expectedCommand);

    private void Complete(IMessageData message)
    {
        _publish(message);
        _tcs.TrySetResult(message);
    }

    public Task<IMessageData?> Result(CancellationToken ct)
    {
        ct.Register(() => _tcs.TrySetCanceled(ct));
        return _tcs.Task;
    }

    public bool IsCompleted => _tcs.Task.IsCompleted;

    public void Dispose() => _tcs.TrySetCanceled();
}
