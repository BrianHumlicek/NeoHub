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

using System.Security.Cryptography;

namespace DSC.TLink.ITv2.Encryption
{
    internal class Type2EncryptionHandler : EncryptionHandler
    {
        private readonly byte[] _integrationAccessCode;

        /// <summary>
        /// Create Type2 encryption handler from per-connection settings
        /// </summary>
        public Type2EncryptionHandler(ConnectionSettings connection)
            : this(connection.IntegrationAccessCodeType2 
                ?? throw new InvalidOperationException("IntegrationAccessCodeType2 is not configured"))
        {
        }

        /// <summary>
        /// Configure type 2 encryption with parameter 'Integration Access Code'
        /// ID's [851][700,701,702,703] for panel integration session 1-4
        /// </summary>
        /// <param name="integrationAccessCode">Type 2 Integration Access Code (32-character hex string)</param>
        public Type2EncryptionHandler(string integrationAccessCode)
        {
            if (string.IsNullOrEmpty(integrationAccessCode))
                throw new ArgumentNullException(nameof(integrationAccessCode));
            
            if (integrationAccessCode.Length != 32)
                throw new ArgumentException("Type 2 integration access code must be 32 hex characters (16 bytes)", nameof(integrationAccessCode));

            try
            {
                _integrationAccessCode = Convert.FromHexString(integrationAccessCode);
            }
            catch (FormatException ex)
            {
                throw new ArgumentException("Type 2 integration access code must be a valid hex string", nameof(integrationAccessCode), ex);
            }
        }

        public override void ConfigureOutboundEncryption(byte[] remoteInitializer)
        {
            if (remoteInitializer == null)
                throw new ArgumentNullException(nameof(remoteInitializer));
            if (remoteInitializer.Length != 16)
                throw new ArgumentException("Remote initializer must be 16 bytes", nameof(remoteInitializer));

            byte[] outboundKey = encryptKeyData(_integrationAccessCode, remoteInitializer);
            activateOutbound(outboundKey);
        }

        public override byte[] ConfigureInboundEncryption()
        {
            byte[] localInitializer = RandomNumberGenerator.GetBytes(16);
            byte[] inboundKey = encryptKeyData(_integrationAccessCode, localInitializer);
            activateInbound(inboundKey);
            return localInitializer;
        }
    }
}
