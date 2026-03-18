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
using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    /// <summary>
    /// Enters configuration / bypass programming mode on the panel (0x0704).
    /// Must be sent before <see cref="SingleZoneBypassWrite"/> (0x074A).
    ///
    /// Wire format (verified from dsc-itv2-client Node.js implementation):
    ///   [CompactInt: Partition][byte: ProgrammingType][CompactIntArray: AccessCode][byte: ReadWriteMode]
    ///
    /// AccessCode encoding: raw digit values, one byte per digit.
    ///   "1234" → [0x01, 0x02, 0x03, 0x04]  (NOT BCD, NOT ASCII)
    /// Panel returns InvalidUserCredentials if BCD or ASCII encoding is used.
    ///
    /// For zone bypass: ProgrammingType = 0x03 (UserBypassProgramming), ReadWriteMode = 0x01.
    /// </summary>
    [ITv2Command(ITv2Command.Configuration_Enter)]
    public record ConfigurationEnter : CommandMessageBase
    {
        [CompactInteger]
        public int Partition { get; init; }

        /// <summary>Programming type. 0x03 = UserBypassProgramming.</summary>
        public byte ProgrammingType { get; init; } = 0x03;

        /// <summary>
        /// Access code as raw digit bytes (one byte per digit, value 0–9).
        /// Serialized with a CompactInteger length prefix.
        /// </summary>
        [CompactIntegerArray]
        public byte[] AccessCode { get; init; } = Array.Empty<byte>();

        /// <summary>Access mode. 0x01 = user code.</summary>
        public byte ReadWriteMode { get; init; } = 0x01;
    }
}
