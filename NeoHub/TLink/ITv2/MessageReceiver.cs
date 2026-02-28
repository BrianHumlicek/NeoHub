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
    private readonly TaskCompletionSource<IMessageData?> _tcs;

    private MessageReceiver(byte senderSequence, byte? commandSequence)
    {
        _senderSequence = senderSequence;
        _commandSequence = commandSequence;
        _tcs = new TaskCompletionSource<IMessageData?>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public static MessageReceiver CreateNotificationReceiver(byte senderSequence)
        => new(senderSequence, commandSequence: null);

    public static MessageReceiver CreateCommandReceiver(byte senderSequence, byte commandSequence)
        => new(senderSequence, commandSequence);

    public bool TryReceive(ITv2Packet packet)
    {
        if (packet.ReceiverSequence == _senderSequence && packet.Message is SimpleAck)
        {
            if (_commandSequence is null)
                _tcs.TrySetResult(null);
            return true;
        }

        if (packet.Message is ICommandMessage commandMessage)
            return TryReceiveSubMessage(commandMessage);

        return false;
    }

    public bool TryReceiveSubMessage(IMessageData message)
    {
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
