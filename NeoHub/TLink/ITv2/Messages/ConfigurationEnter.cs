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
    ///   [CompactInt: Partition][byte: ProgrammingType][1B-length: AccessCode][byte: ReadWriteMode]
    ///
    /// AccessCode encoding: raw digit values, one byte per digit.
    ///   "1234" → [0x01, 0x02, 0x03, 0x04]  (NOT BCD, NOT ASCII)
    /// Panel returns InvalidUserCredentials if BCD or ASCII encoding is used.
    ///
    /// For zone bypass: ProgrammingMode = UserBypassProgramming, AccessMode = UserCode.
    /// </summary>
    [ITv2Command(ITv2Command.Configuration_Enter)]
    public record ConfigurationEnter : CommandMessageBase
    {
        [CompactInteger]
        public int Partition { get; init; } = 1;

        public ProgrammingMode ProgrammingMode { get; init; }

        /// <summary>
        /// Access code as raw digit bytes (one byte per digit, value 0–9).
        /// Serialized with a single leading length byte: [len][digits...].
        /// </summary>
        [LeadingLengthBCDString]
        public string AccessCode { get; init; } = string.Empty;

        public ReadWriteAccessEnum ReadWrite { get; init; }
        public enum ReadWriteAccessEnum : byte
        {
            ReadOnlyMode,
            ReadWriteMode
        }
    }
}
