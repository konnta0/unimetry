using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Unimetry.Internal
{
    /// <summary>
    /// Encrypts the offline queue with AES-256-CBC and HMAC-SHA256.
    /// The key file lives next to the queue. Copying both files defeats the protection;
    /// it stops the queue from being stored as readable JSON.
    /// </summary>
    internal static class OfflineProtector
    {
        private static readonly byte[] Magic = { (byte)'U', (byte)'M', (byte)'Q', (byte)'1' };
        private const int AesKeyBytes = 32;
        private const int HmacKeyBytes = 32;
        private const int KeyBytes = AesKeyBytes + HmacKeyBytes;
        private const int IvBytes = 16;
        private const int HmacBytes = 32;

        public static byte[] Protect(string json, string keyPath)
        {
            var key = LoadOrCreateKey(keyPath);
            if (key == null)
            {
                return Encoding.UTF8.GetBytes(json ?? string.Empty);
            }

            var plain = Encoding.UTF8.GetBytes(json ?? string.Empty);
            var iv = new byte[IvBytes];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(iv);
            }

            byte[] cipher;
            using (var aes = Aes.Create())
            {
                aes.Key = CopyRange(key, 0, AesKeyBytes);
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using var encryptor = aes.CreateEncryptor();
                cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
            }

            var body = new byte[Magic.Length + IvBytes + cipher.Length];
            Buffer.BlockCopy(Magic, 0, body, 0, Magic.Length);
            Buffer.BlockCopy(iv, 0, body, Magic.Length, IvBytes);
            Buffer.BlockCopy(cipher, 0, body, Magic.Length + IvBytes, cipher.Length);
            var mac = ComputeMac(key, body);
            var output = new byte[body.Length + HmacBytes];
            Buffer.BlockCopy(body, 0, output, 0, body.Length);
            Buffer.BlockCopy(mac, 0, output, body.Length, HmacBytes);
            return output;
        }

        public static string Unprotect(byte[] payload, string keyPath)
        {
            if (payload == null || payload.Length == 0)
            {
                return string.Empty;
            }

            if (payload[0] == (byte)'{')
            {
                return Encoding.UTF8.GetString(payload);
            }

            if (payload.Length < Magic.Length + IvBytes + HmacBytes || !HasMagic(payload))
            {
                return null;
            }

            var key = LoadOrCreateKey(keyPath);
            if (key == null)
            {
                return null;
            }

            var bodyLength = payload.Length - HmacBytes;
            var body = new byte[bodyLength];
            Buffer.BlockCopy(payload, 0, body, 0, bodyLength);
            var expected = ComputeMac(key, body);
            var actual = new byte[HmacBytes];
            Buffer.BlockCopy(payload, bodyLength, actual, 0, HmacBytes);
            if (!FixedTimeEquals(expected, actual))
            {
                return null;
            }

            var iv = new byte[IvBytes];
            Buffer.BlockCopy(payload, Magic.Length, iv, 0, IvBytes);
            var cipherLength = bodyLength - Magic.Length - IvBytes;
            var cipher = new byte[cipherLength];
            Buffer.BlockCopy(payload, Magic.Length + IvBytes, cipher, 0, cipherLength);
            using var aes = Aes.Create();
            aes.Key = CopyRange(key, 0, AesKeyBytes);
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var decryptor = aes.CreateDecryptor();
            var plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
            return Encoding.UTF8.GetString(plain);
        }

        private static byte[] LoadOrCreateKey(string keyPath)
        {
            try
            {
                if (File.Exists(keyPath))
                {
                    var existing = File.ReadAllBytes(keyPath);
                    return existing.Length == KeyBytes ? existing : null;
                }

                var directory = Path.GetDirectoryName(keyPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var created = new byte[KeyBytes];
                using (var random = RandomNumberGenerator.Create())
                {
                    random.GetBytes(created);
                }

                File.WriteAllBytes(keyPath, created);
                return created;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static byte[] ComputeMac(byte[] key, byte[] body)
        {
            using var hmac = new HMACSHA256(CopyRange(key, AesKeyBytes, HmacKeyBytes));
            return hmac.ComputeHash(body);
        }

        private static byte[] CopyRange(byte[] source, int offset, int count)
        {
            var copy = new byte[count];
            Buffer.BlockCopy(source, offset, copy, 0, count);
            return copy;
        }

        private static bool HasMagic(byte[] payload)
        {
            for (var index = 0; index < Magic.Length; index++)
            {
                if (payload[index] != Magic[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            var difference = 0;
            for (var index = 0; index < left.Length; index++)
            {
                difference |= left[index] ^ right[index];
            }

            return difference == 0;
        }
    }
}
