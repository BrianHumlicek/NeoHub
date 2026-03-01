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

namespace DSC.TLink.ITv2.Messages;

/// <summary>
/// Abstract base class for messages that participate in command-level transactions.
/// 
/// The CommandSequence property is serialized by BinarySerializer as the first field,
/// matching the wire protocol format (appears immediately after the message type word).
/// It's declared here so it serializes before any subclass properties (metadata token ordering).
/// </summary>
public abstract record CommandMessageBase : IMessageData, ICommandMessage
{
    /// <summary>
    /// The command sequence byte for correlating requests with responses.
    /// Managed by the session, serialized automatically by BinarySerializer.
    /// </summary>
    public byte CommandSequence { get; set; }
}
