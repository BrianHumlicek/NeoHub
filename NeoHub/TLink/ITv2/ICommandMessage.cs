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

/// <summary>
/// Marker interface for command messages that participate in command-level transactions.
/// Command messages have a CommandSequence byte for correlation between requests and responses.
/// 
/// This interface is explicitly implemented by abstract base classes to hide protocol
/// details from public APIs while allowing the session to access the CommandSequence.
/// </summary>
internal interface ICommandMessage : IMessageData
{
    /// <summary>
    /// The command sequence byte used to correlate command requests with their responses.
    /// This is a shared counter incremented by both nodes.
    /// </summary>
    byte CommandSequence { get; set; }
}
