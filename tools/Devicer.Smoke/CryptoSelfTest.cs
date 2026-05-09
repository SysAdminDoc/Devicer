using System.Security.Cryptography;
using System.Text;

namespace Devicer.Smoke;

internal static class CryptoSelfTest
{
    public static void RoundTrip()
    {
        // Sanity: encrypt a known plaintext with KEY_1, then decrypt — should recover.
        const string Key1 = "hqzdurufm2c8mf6bsjezu1qgveouv7c7";
        var key = Encoding.UTF8.GetBytes(Key1);
        var iv = key.AsSpan(0, 16).ToArray();

        var plain = Encoding.ASCII.GetBytes("ABCDEFGHIJKLMNOP"); // exactly 16 ASCII chars

        using (var aes = Aes.Create())
        {
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            aes.IV = iv;
            using var enc = aes.CreateEncryptor();
            var ct = enc.TransformFinalBlock(plain, 0, plain.Length);
            var b64 = Convert.ToBase64String(ct);
            Console.WriteLine($"Round-trip ENC: plain='{Encoding.ASCII.GetString(plain)}' -> b64 ct len {b64.Length}: {b64}");
            using var dec = aes.CreateDecryptor();
            var pt = dec.TransformFinalBlock(ct, 0, ct.Length);
            Console.WriteLine($"Round-trip DEC: '{Encoding.ASCII.GetString(pt)}' (len {pt.Length})");
        }

        // Decrypt a real Samsung NONCE just captured via curl.
        const string CapturedNonce = "SRW97M7Aa/XqTIsBa7VclNoX8WQFRrDtnHDqybB4hGc=";
        TryDecodeAndPrint(CapturedNonce, "hqzdurufm2c8mf6bsjezu1qgveouv7c7", "current");
        TryDecodeAndPrint(CapturedNonce, "vicopx7dqu06emacgpnpy8j8zwhduwlh", "legacy");
    }

    private static void TryDecodeAndPrint(string b64, string keyStr, string label)
    {
        var ct = Convert.FromBase64String(b64);
        var k = Encoding.UTF8.GetBytes(keyStr);
        var iv = k.AsSpan(0, 16).ToArray();
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key = k;
        aes.IV = iv;
        using var dec = aes.CreateDecryptor();
        try
        {
            var pt = dec.TransformFinalBlock(ct, 0, ct.Length);
            var ascii = Encoding.ASCII.GetString(pt.AsSpan(0, Math.Min(16, pt.Length)));
            var hex = Convert.ToHexString(pt);
            Console.WriteLine($"key={label}: ascii='{ascii}' hex={hex}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"key={label}: failed: {ex.Message}");
        }
    }
}
