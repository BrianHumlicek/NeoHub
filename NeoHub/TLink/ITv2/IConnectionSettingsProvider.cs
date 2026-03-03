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

namespace DSC.TLink.ITv2;

/// <summary>
/// Resolves per-connection settings for incoming panel connections.
/// Called mid-handshake after the panel identifies itself.
/// </summary>
public interface IConnectionSettingsProvider
{
    /// <summary>
    /// Resolves connection settings for a panel that identified itself during handshake.
    /// <para>
    /// If the session ID is unknown, creates a placeholder entry (with encryption type
    /// pre-populated) and returns <c>null</c>.
    /// If settings exist but are incomplete, returns <c>null</c>.
    /// Returns settings only when they are complete and valid for use.
    /// </para>
    /// </summary>
    ConnectionSettings? ResolveConnection(string sessionId, EncryptionType encryptionType);
}
