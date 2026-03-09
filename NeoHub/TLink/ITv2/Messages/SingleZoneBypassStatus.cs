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
	/// Real-time notification from the panel indicating a zone's bypass state has changed.
	/// Sent after a bypass/unbypass operation (from keypad, ITv2, or DLS).
	/// 
	/// Observed wire format (payload after command word):
	///   [CompactInt: ZoneNumber] [byte: BypassState]
	///   [01-05-00] = Zone 5 unbypassed
	///   [01-05-01] = Zone 5 bypassed
	/// </summary>
	[ITv2Command(ITv2Command.ModuleStatus_Single_Zone_Bypass_Status)]
	public record SingleZoneBypassStatus : IMessageData
	{
		[CompactInteger]
		public byte ZoneNumber { get; init; }

		/// <summary>
		/// 0 = zone is not bypassed (normal monitoring),
		/// 1 = zone is bypassed (excluded from monitoring).
		/// </summary>
		public BypassStatusEnum BypassStatus { get; init; }

        public enum BypassStatusEnum : byte
        {
            NotBypassed = 0,
            Bypassed = 1
        }
    }
}
