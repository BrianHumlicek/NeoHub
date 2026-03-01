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
	internal class Type1EncryptionHandler : EncryptionHandler
	{
		private readonly byte[] _integrationAccessCode;
		private readonly byte[] _integrationIdentificationNumber;

		/// <summary>
		/// Create Type1 encryption handler from configuration
		/// </summary>
		public Type1EncryptionHandler(ITv2Settings settings)
			: this(
				settings.IntegrationAccessCodeType1 
					?? throw new InvalidOperationException("IntegrationAccessCodeType1 is not configured"),
				settings.IntegrationIdentificationNumber 
					?? throw new InvalidOperationException("IntegrationIdentificationNumber is not configured"))
		{
		}

		/// <summary>
		/// This constructor sets up Type 1 encryption
		/// </summary>
		/// <param name="integrationAccessCode">Type 1 Integration Access Code [851][423,450,477,504]</param>
		/// <param name="integrationIdentificationNumber">Integration Identification Number [851][422]</param>
		public Type1EncryptionHandler(string integrationAccessCode, string integrationIdentificationNumber)
		{
			if (string.IsNullOrEmpty(integrationAccessCode))
				throw new ArgumentNullException(nameof(integrationAccessCode));
			if (string.IsNullOrEmpty(integrationIdentificationNumber))
				throw new ArgumentNullException(nameof(integrationIdentificationNumber));
			
			if (integrationAccessCode.Length < 8)
				throw new ArgumentException("Integration access code must be at least 8 characters", nameof(integrationAccessCode));
			
			// This parameter is 12 digits long, but we only need the first 8 digits so that is all that is being enforced here.
			if (integrationIdentificationNumber.Length < 8)
				throw new ArgumentException("Integration identification number must be at least 8 characters", nameof(integrationIdentificationNumber));

			_integrationAccessCode = TransformKeyString(integrationAccessCode);
			_integrationIdentificationNumber = TransformKeyString(integrationIdentificationNumber);
		}

		private static byte[] TransformKeyString(string keyString)
		{
			string first8 = keyString.Substring(0, 8);
			// This makes a 32 digit base 10 string, and then reads it as base 16 string which makes a 16 byte array.
			return Convert.FromHexString($"{first8}{first8}{first8}{first8}");
		}

		private static IEnumerable<byte> EvenIndexes(IEnumerable<byte> bytes) 
			=> bytes.Where((element, index) => index % 2 == 0);
		
		private static IEnumerable<byte> OddIndexes(IEnumerable<byte> bytes) 
			=> bytes.Where((element, index) => index % 2 == 1);

		public override void ConfigureOutboundEncryption(byte[] remoteInitializer)
		{
			if (remoteInitializer == null)
				throw new ArgumentNullException(nameof(remoteInitializer));
			if (remoteInitializer.Length != 48)
				throw new ArgumentException("Remote initializer must be 48 bytes for Type1 encryption", nameof(remoteInitializer));

			var checkBytes = remoteInitializer.Take(16);
			var cipherText = remoteInitializer.Skip(16).Take(32).ToArray();

			byte[] plainText = decryptKeyData(_integrationIdentificationNumber, cipherText);

			if (!checkBytes.SequenceEqual(EvenIndexes(plainText)))
				throw new InvalidOperationException("Encryption initializer check byte failure.");

			byte[] outboundKey = OddIndexes(plainText).ToArray();

			activateOutbound(outboundKey);
		}

		public override byte[] ConfigureInboundEncryption()
		{
			byte[] randomBytes = RandomNumberGenerator.GetBytes(32);

			var checkBytes = EvenIndexes(randomBytes);

			byte[] inboundKey = OddIndexes(randomBytes).ToArray();

			activateInbound(inboundKey);

			byte[] cipherText = encryptKeyData(_integrationAccessCode, randomBytes);

			return checkBytes.Concat(cipherText).ToArray();
		}
	}
}
