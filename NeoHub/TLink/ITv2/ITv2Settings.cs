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

namespace DSC.TLink.ITv2
{
    /// <summary>
    /// Strongly-typed configuration model for TLink settings.
    /// Binds to the "TLink" section in appsettings.json / userSettings.json
    /// </summary>
    [Display(Name = "DSC TLink Settings", Description = "Configuration for DSC PowerSeries NEO panel integration")]
    public class ITv2Settings
    {
        public const string SectionName = "DSC.TLink";
        public const int DefaultListenPort = 3072;

        /// <summary>
        /// Integration Access Code for Type1 encryption (8 digit code)
        /// </summary>
        [Display(
            Name = "Type 1 Access Code",
            Description = "8-digit integration code for Type 1 encryption [851][423,450,477,504]",
            GroupName = "Encryption",
            Order = 1)]
        [Required]
        public string? IntegrationAccessCodeType1 { get; set; } = "12345678";

        /// <summary>
        /// Integration Access Code for Type2 encryption (32-character hex string)
        /// </summary>
        [Display(
            Name = "Type 2 Access Code",
            Description = "32-character hex string for Type 2 encryption [851][700,701,702,703]",
            GroupName = "Encryption",
            Order = 2)]
        public string? IntegrationAccessCodeType2 { get; set; } = "12345678123456781234567812345678";

        /// <summary>
        /// Integration Identification Number (dealer ID)
        /// </summary>
        [Display(
            Name = "Integration ID",
            Description = "12-digit dealer/integrator identification number [851][422]",
            GroupName = "Encryption",
            Order = 3)]
        [Required]
        public string? IntegrationIdentificationNumber { get; set; } = "";

        /// <summary>
        /// TCP port for panel connections (default: 3072)
        /// </summary>
        [Display(
            Name = "Server Port",
            Description = "TCP port for panel connections",
            GroupName = "Network",
            Order = 10)]
        [Range(1, 65535)]
        public int ListenPort { get; set; } = DefaultListenPort;

        /// <summary>
        /// Maximum number of zones to pull from the panel.
        /// The actual count used is the lesser of this value and the panel's reported max.
        /// Set to 0 or omit to use the panel's reported max.
        /// </summary>
        [Display(
            Name = "Max Zones",
            Description = "Limit how many zones to pull (0 = use panel max)",
            GroupName = "Network",
            Order = 11)]
        [Range(0, 256)]
        public int MaxZones { get; set; }
    }
}