using System;
using System.Collections.Generic;

namespace RemoteCore
{
    /// <summary>
    /// Base32 encoder/decoder using a custom alphabet that excludes ambiguous characters.
    /// Alphabet: ABCDEFGHJKLMNPQRSTUVWXYZ23456789
    /// Produces/consumes upper-case letters and digits only.
    /// </summary>
    public static class Base32Custom
    {
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // 32 chars
        private static readonly Dictionary<char, int> _reverse;

        static Base32Custom()
        {
            _reverse = new Dictionary<char, int>(32);
            for (int i = 0; i < Alphabet.Length; i++)
                _reverse[Alphabet[i]] = i;
        }

        public static string Encode(byte[] data)
        {
            if (data == null || data.Length == 0)
                return string.Empty;

            // Convert to bit stream and emit 5-bit groups
            var bits = new List<int>(data.Length * 8);
            foreach (var b in data)
            {
                for (int i = 7; i >= 0; i--)
                    bits.Add((b >> i) & 1);
            }

            // pad bits to multiple of 5 with zero bits (no '=' padding in output)
            while (bits.Count % 5 != 0)
                bits.Add(0);

            var outChars = new List<char>(bits.Count / 5);
            for (int i = 0; i < bits.Count; i += 5)
            {
                int val = 0;
                for (int j = 0; j < 5; j++)
                    val = (val << 1) | bits[i + j];
                outChars.Add(Alphabet[val]);
            }

            return new string(outChars.ToArray());
        }

        public static byte[] Decode(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<byte>();

            text = text.Trim().ToUpperInvariant();

            // validate characters
            foreach (var c in text)
            {
                if (!_reverse.ContainsKey(c))
                    throw new FormatException($"Invalid Base32 character: '{c}'");
            }

            var bits = new List<int>(text.Length * 5);
            foreach (var c in text)
            {
                int v = _reverse[c];
                for (int i = 4; i >= 0; i--)
                    bits.Add((v >> i) & 1);
            }

            // Trim trailing zero bits to nearest byte boundary
            int extra = bits.Count % 8;
            if (extra != 0)
            {
                // remove the padded zeros that were added during encoding
                bits.RemoveRange(bits.Count - extra, extra);
            }

            var outBytes = new List<byte>(bits.Count / 8);
            for (int i = 0; i < bits.Count; i += 8)
            {
                int val = 0;
                for (int j = 0; j < 8; j++)
                    val = (val << 1) | bits[i + j];
                outBytes.Add((byte)val);
            }

            return outBytes.ToArray();
        }
    }
}
