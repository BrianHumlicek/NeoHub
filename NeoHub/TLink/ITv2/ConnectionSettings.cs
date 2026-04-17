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

using System.ComponentModel.DataAnnotations;
using DSC.TLink.ITv2.Enumerations;

namespace DSC.TLink.ITv2;

/// <summary>
/// Per-connection settings for an individual alarm panel.
/// The <see cref="SessionId"/> is the primary key, matching the panel's
/// Integration Identification Number [851][422].
/// </summary>
public class ConnectionSettings
{
    /// <summary>
    /// Default TCP port for ITv2 panel connections per the TLink protocol.
    /// </summary>
    public const int DefaultListenPort = 3072;

    /// <summary>DSC factory default for Type 1 encryption access code.</summary>
    public const string FactoryDefaultType1 = "12345678";

    /// <summary>DSC factory default for Type 2 encryption access code.</summary>
    public const string FactoryDefaultType2 = "12345678123456781234567812345678";

    /// <summary>
    /// Logging scope key used to tag log entries with a session identifier.
    /// </summary>
    public const string LogScopeKey = nameof(SessionId);

    /// <summary>
    /// The panel's Integration Identification Number [851][422].
    /// Sent by the panel during the handshake — acts as the unique key for this connection.
    /// </summary>
    [Display(
        Name = "Session ID",
        Description = "Integration Identification Number [851][422]",
        Order = 0)]
    public string SessionId { get; set; } = "";

    /// <summary>
    /// Encryption type used by this panel connection.
    /// Populated automatically when a panel first connects.
    /// </summary>
    [Display(
        Name = "Encryption Type",
        Description = "Encryption type negotiated by the panel during handshake",
        Order = 1)]
    public EncryptionType EncryptionType { get; set; } = EncryptionType.Unknown;

    /// <summary>
    /// Integration Access Code for Type 1 encryption (8-digit code) [851][423,450,477,504]
    /// </summary>
    [Display(
        Name = "Type 1 Access Code",
        Description = "8-digit integration code for Type 1 encryption [851][423,450,477,504]",
        GroupName = "Encryption",
        Order = 2)]
    public string? IntegrationAccessCodeType1 { get; set; }

    /// <summary>
    /// Integration Access Code for Type 2 encryption (32-character hex string) [851][700,701,702,703]
    /// </summary>
    [Display(
        Name = "Type 2 Access Code",
        Description = "32-character hex string for Type 2 encryption [851][700,701,702,703]",
        GroupName = "Encryption",
        Order = 3)]
    public string? IntegrationAccessCodeType2 { get; set; }

    /// <summary>
    /// Default access code for one-touch arm/disarm operations (optional).
    /// If set, allows arming and disarming without entering a code each time.
    /// DSC factory default is 1234.
    /// </summary>
    [Display(
        Name = "Default Access Code",
        Description = "Access code for one-touch arm/disarm/bypass (factory default: 1234)",
        GroupName = "Panel Control",
        Order = 4)]
    [RegularExpression(@"^\d*$", ErrorMessage = "Access code must contain digits only")]
    public string? DefaultAccessCode { get; set; }

    /// <summary>
    /// Installer code for accessing panel configuration sections (optional).
    /// If set, the full installer configuration is read automatically on connect.
    /// DSC factory default is 5555.
    /// </summary>
    [Display(
        Name = "Installer Code",
        Description = "Installer code for configuration access (factory default: 5555)",
        GroupName = "Panel Control",
        Order = 5)]
    [RegularExpression(@"^\d*$", ErrorMessage = "Installer code must contain digits only")]
    public string? InstallerCode { get; set; }

    /// <summary>
    /// Master code for user management operations (optional).
    /// Required for adding, editing, or removing user codes on the panel.
    /// DSC factory default is 1234.
    /// </summary>
    [Display(
        Name = "Master Code",
        Description = "Master code for user management (factory default: 1234)",
        GroupName = "Panel Control",
        Order = 6)]
    [RegularExpression(@"^\d*$", ErrorMessage = "Master code must contain digits only")]
    public string? MasterCode { get; set; }

    /// <summary>
    /// Maximum number of zones to pull from this panel.
    /// The actual count used is the lesser of this value and the panel's reported max.
    /// Set to 0 or omit to use the panel's reported max.
    /// </summary>
    [Display(
        Name = "Max Zones",
        Description = "Limit how many zones to pull (0 = use panel max)",
        Order = 10)]
    [Range(0, 256)]
    public int MaxZones { get; set; }

    /// <summary>
    /// Whether this connection has all required settings filled in.
    /// Incomplete connections (e.g., auto-created placeholders) will be rejected.
    /// </summary>
    public bool IsComplete => !string.IsNullOrWhiteSpace(SessionId) && HasEncryptionKey(EncryptionType);

    /// <summary>
    /// Checks whether the required encryption access code is configured for the given type.
    /// Used during handshake to verify settings match the panel's actual encryption requirement.
    /// </summary>
    public bool HasEncryptionKey(EncryptionType type) => type switch
    {
        EncryptionType.Type1 => !string.IsNullOrWhiteSpace(IntegrationAccessCodeType1),
        EncryptionType.Type2 => !string.IsNullOrWhiteSpace(IntegrationAccessCodeType2),
        _ => false
    };
}
