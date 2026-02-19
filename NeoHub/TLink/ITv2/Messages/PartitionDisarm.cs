// DSC TLink - a communications library for DSC Powerseries NEO alarm panels
// Copyright (C) 2024 Brian Humlicek
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY, without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.ITv2.Transactions;
using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
	[ITv2Command(ITv2Command.ModuleControl_Partition_Disarm_Control)]
	[SimpleAckTransaction]
	public record PartitionDisarm : ICommandMessageData
	{
		public byte CorrelationID { get; set; }
		[CompactInteger]
		public int Partition { get; init; }
		[BCDString]
		public string AccessCode { get; init; } = String.Empty;
	}
}
