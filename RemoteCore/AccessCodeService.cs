using System;
using System.Net;
using System.Security.Cryptography;

namespace RemoteCore
{
    public class AccessCodeService
    {
        private readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();

        /// <summary>
        /// Generate a 6-character access code using:
        /// - last octet of IPv4 (0-255)
        /// - session token (ushort)
        /// - checksum (XOR)
        /// Encoded with Base32Custom alphabet (uppercase letters and digits only)
        /// </summary>
        public string GenerateCode(IPAddress localIpv4, ushort sessionToken)
        {
            if (localIpv4 == null)
                throw new ArgumentNullException(nameof(localIpv4));

            var bytes = localIpv4.GetAddressBytes();
            if (bytes.Length != 4)
                throw new ArgumentException("IPv4 address expected", nameof(localIpv4));

            byte last = bytes[3]; // 0-255

            // prepare payload: 1 byte lastOctet, 2 bytes token (big endian)
            var payload = new byte[3];
            payload[0] = last;
            payload[1] = (byte)(sessionToken >> 8);
            payload[2] = (byte)(sessionToken & 0xFF);

            // checksum: XOR of payload bytes -> 1 byte
            byte checksum = (byte)(payload[0] ^ payload[1] ^ payload[2]);

            // final blob: 4 bytes (last, token hi, token lo, checksum)
            var blob = new byte[4];
            Array.Copy(payload, 0, blob, 0, 3);
            blob[3] = checksum;

            // We need to produce 6 base32 characters. 6 * 5 = 30 bits. We have 32 bits -> OK.
            // Encode the 4 bytes using Base32Custom and take first 6 chars.
            var encoded = Base32Custom.Encode(blob);
            if (encoded.Length < 6)
            {
                // pad (should not happen)
                encoded = encoded.PadRight(6, 'A');
            }

            return encoded.Substring(0, 6);
        }

        /// <summary>
        /// Decode code into components: lastOctet, sessionToken, checksum. Returns true if checksum matches.
        /// </summary>
        public bool TryDecode(string code, out byte lastOctet, out ushort sessionToken)
        {
            lastOctet = 0;
            sessionToken = 0;
            if (string.IsNullOrWhiteSpace(code))
                return false;

            code = code.Trim().ToUpperInvariant();
            try
            {
                // decode, we expect at least 4 bytes
                var blob = Base32Custom.Decode(code);
                if (blob.Length < 4)
                    return false;

                lastOctet = blob[0];
                sessionToken = (ushort)((blob[1] << 8) | blob[2]);
                var checksum = blob[3];
                return checksum == (byte)(blob[0] ^ blob[1] ^ blob[2]);
            }
            catch
            {
                return false;
            }
        }
    }
}
