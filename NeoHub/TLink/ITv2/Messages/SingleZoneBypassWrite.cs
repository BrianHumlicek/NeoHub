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
    /// Bypasses or unbypasses a single zone (0x074A).
    /// Must be sent while in bypass programming mode (after <see cref="ConfigurationEnter"/>).
    /// The panel confirms via <see cref="SingleZoneBypassStatus"/> (0x0820) notification.
    ///
    /// Wire format (verified from dsc-itv2-client Node.js implementation):
    ///   [CompactInt: Partition][CompactInt: ZoneNumber][byte: BypassState]
    ///
    /// BypassState: 0x01 = bypass, 0x00 = unbypass.
    /// </summary>
    [ITv2Command(ITv2Command.Configuration_Write_Single_Zone_Bypass_Write)]
    public record SingleZoneBypassWrite : CommandMessageBase
    {
        [CompactInteger]
        public int Partition { get; init; }

        [CompactInteger]
        public int ZoneNumber { get; init; }
        public BypassStatusEnum BypassState { get; init; }
    }
}
