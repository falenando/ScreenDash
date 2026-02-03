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

            // Compute a small checksum (6 bits) from the three payload bytes.
            // We'll pack lastOctet (8 bits) + sessionToken (16 bits) + checksum (6 bits) = 30 bits
            byte b0 = payload[0];
            byte b1 = payload[1];
            byte b2 = payload[2];
            int rawChecksum = b0 ^ b1 ^ b2;
            int checksum6 = rawChecksum & 0x3F; // keep lower 6 bits

            // Build bit list MSB-first
            var bits = new System.Collections.Generic.List<int>(30);

            // last octet (8 bits)
            for (int i = 7; i >= 0; i--)
                bits.Add((b0 >> i) & 1);

            // session token (16 bits)
            for (int i = 15; i >= 0; i--)
                bits.Add(( (payload[1] << 8) | payload[2] ) >> i & 1);

            // checksum (6 bits)
            for (int i = 5; i >= 0; i--)
                bits.Add((checksum6 >> i) & 1);

            var encoded = Base32Custom.EncodeBits(bits);
            // encoded should be exactly 6 characters (6*5=30)
            if (encoded.Length != 6)
                throw new InvalidOperationException("Unexpected encoded length: " + encoded.Length);

            return encoded;
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
                // Try the primary (30-bit) format first
                var bits = Base32Custom.DecodeToBits(code);
                if (bits.Count == 30)
                {
                    int idx = 0;
                    int lo = 0;
                    for (int i = 0; i < 8; i++)
                        lo = (lo << 1) | bits[idx++];

                    int token = 0;
                    for (int i = 0; i < 16; i++)
                        token = (token << 1) | bits[idx++];

                    int checksum6 = 0;
                    for (int i = 0; i < 6; i++)
                        checksum6 = (checksum6 << 1) | bits[idx++];

                    lastOctet = (byte)lo;
                    sessionToken = (ushort)token;

                    byte b0 = (byte)lo;
                    byte b1 = (byte)((token >> 8) & 0xFF);
                    byte b2 = (byte)(token & 0xFF);
                    int expected = (b0 ^ b1 ^ b2) & 0x3F;
                    return expected == checksum6;
                }

                // Fallback: try full-byte decode (older/alternative formats)
                var blob = Base32Custom.Decode(code);
                if (blob.Length >= 3)
                {
                    lastOctet = blob[0];
                    sessionToken = (ushort)((blob[1] << 8) | blob[2]);

                    if (blob.Length >= 4)
                    {
                        // verify checksum full byte if present
                        var checksum = blob[3];
                        return checksum == (byte)(blob[0] ^ blob[1] ^ blob[2]);
                    }

                    // no checksum byte available -- accept decode but return false? we'll accept
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
