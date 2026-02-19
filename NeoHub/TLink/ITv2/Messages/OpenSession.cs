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
using DSC.TLink.ITv2.Transactions;
using DSC.TLink.Serialization;
using MediatR;


namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(ITv2Command.Connection_Open_Session)]
    [CommandResponseTransaction]
    public record OpenSession : CommandMessageData
    {
        public Itv2PanelDeviceType DeviceType { get; init; }
        
        [FixedArray(2)]
        public byte[] DeviceID { get; init; } = new byte[2];
        
        [FixedArray(2)]
        public byte[] FirmwareVersion { get; init; } = new byte[2];
        
        public ProtocolCompatibility ProtocolVersion { get; init; }
        public ushort TxBufferSize { get; init; }
        public ushort RxBufferSize { get; init; }
        
        [FixedArray(2)]
        public byte[] Unknown { get; init; } = new byte[2];
        
        public EncryptionType EncryptionType { get; init; }

        // Calculated properties - ignored by serializer
        [IgnoreProperty]
        public int FirmwareVersionNumber => FirmwareVersion.Length >= 2 
            ? (FirmwareVersion[0] << 4 | FirmwareVersion[1] >> 4) 
            : 0;
        
        [IgnoreProperty]
        public int FirmwareRevisionNumber => FirmwareVersion.Length >= 2 
            ? (FirmwareVersion[1] & 0x0F) 
            : 0;
        public override bool IsAsynchronous => false;
    }
}