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

namespace DSC.TLink;

/// <summary>
/// Error codes for TLink protocol operations, organized by layer.
/// Carried by <see cref="TLinkError"/> inside a failed <see cref="Result"/> or <see cref="Result{T}"/>.
/// Panel command rejections are separate — they remain in <see cref="ITv2.Enumerations.CommandResponseCode"/>
/// on the returned <see cref="ITv2.Messages.IMessageData"/>.
/// </summary>
public enum TLinkErrorCode
{
    /// <summary>The I/O operation was cancelled via CancellationToken.</summary>
    Cancelled,

    /// <summary>The remote endpoint closed the connection.</summary>
    Disconnected,

    /// <summary>A TLink wire frame was malformed — missing or misplaced delimiters.</summary>
    FramingError,

    /// <summary>Byte-stuffing encoding was invalid — illegal bytes or unknown escape values.</summary>
    EncodingError,

    /// <summary>A cryptographic operation (AES encrypt/decrypt) failed.</summary>
    EncryptionError,

    /// <summary>
    /// An ITv2 packet could not be parsed — includes CRC mismatch, invalid length,
    /// unsupported message type, or binary deserialization failure.
    /// </summary>
    PacketParseError,

    /// <summary>No active session was found with the requested session ID.</summary>
    SessionNotFound,
}
