using System.Security.Cryptography;
using System.Text;

namespace Devicer.Core.Services;

/// <summary>
/// Wire-protocol cryptography primitives for Samsung's FUS (Firmware Update Server).
///
/// These are documented protocol facts published in numerous public reverse-engineering
/// repositories (samloader, Bifrost, SamloaderKotlin, samfirm). The constants and
/// algorithms here are wire-format constants — re-implementing them is required for any
/// independent client and is not derivative of any specific source codebase.
///
/// Two AES modes are used in this protocol:
///   • CBC + PKCS#7 — for the auth nonce decode and the request-signature (this file).
///   • ECB + PKCS#7 — for the encrypted firmware blob itself (see <see cref="FirmwareCipher"/>).
/// </summary>
internal static class FusCrypto
{
    // Wire-protocol constants. Not creative expression; required by every independent FUS
    // client. Two key generations exist — current (samloader 0.4+) and legacy (samloader 0.3 era).
    // Samsung's CDN sometimes serves nonces encrypted with the legacy key depending on
    // route / client UA, so we try both on decode.
    internal const string Key1Current = "hqzdurufm2c8mf6bsjezu1qgveouv7c7";
    internal const string Key1Legacy = "vicopx7dqu06emacgpnpy8j8zwhduwlh";
    internal const string Key2 = "w13r4cvf4hctaujv";

    // Backwards-compat alias — most call sites still use this.
    internal const string Key1 = Key1Current;

    /// <summary>
    /// Decrypts a Base64-encoded NONCE from a FUS server response. The decryption key is
    /// the constant <see cref="Key1"/> (UTF-8 bytes), with the IV equal to the first 16 bytes
    /// of that key. The result is a 16-character ASCII string — the server's session nonce.
    /// </summary>
    /// <summary>
    /// Decoded nonce + the KEY_1 generation that successfully decoded it. The same generation
    /// must be used for signature derivation downstream — the signature key is indexed into
    /// the same KEY_1 that produced the nonce.
    /// </summary>
    public sealed record NonceDecode(string Nonce, string Key1Used);

    public static NonceDecode DecryptNonceWithKey(string base64Nonce)
    {
        var encrypted = Convert.FromBase64String(base64Nonce);

        // Samsung's CDN serves nonces under either the current or the legacy KEY_1, depending
        // on route/UA. Try both, prefer whichever yields printable ASCII (the real nonce always is).
        if (TryDecryptWith(encrypted, Key1Current, out var n1) && IsPrintableNonceBytes(n1))
            return new NonceDecode(Encoding.ASCII.GetString(n1!), Key1Current);
        if (TryDecryptWith(encrypted, Key1Legacy, out var n2) && IsPrintableNonceBytes(n2))
            return new NonceDecode(Encoding.ASCII.GetString(n2!), Key1Legacy);
        if (n1 is not null) return new NonceDecode(Encoding.ASCII.GetString(n1), Key1Current);
        throw new CryptographicException("Could not decrypt FUS nonce with any known KEY_1 generation.");
    }

    public static string DecryptNonce(string base64Nonce) => DecryptNonceWithKey(base64Nonce).Nonce;

    private static bool TryDecryptWith(byte[] encrypted, string key, out byte[]? nonceBytes)
    {
        nonceBytes = null;
        try
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            var ivBytes = keyBytes.AsSpan(0, 16).ToArray();
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None; // We only care about the first 16 bytes.
            aes.Key = keyBytes;
            aes.IV = ivBytes;
            using var decryptor = aes.CreateDecryptor();
            var decrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
            var first16 = decrypted.Length >= 16 ? decrypted.AsSpan(0, 16) : decrypted.AsSpan();
            nonceBytes = first16.ToArray();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPrintableNonceBytes(byte[]? bytes)
    {
        if (bytes is null || bytes.Length != 16) return false;
        foreach (var b in bytes)
        {
            // Real Samsung nonces are printable ASCII (alphanumeric + a few symbols).
            // Inspecting the raw bytes (not a decoded string) avoids ASCII's `?`-substitution
            // for high bytes, which would otherwise let garbage masquerade as printable.
            if (b < 0x20 || b > 0x7E) return false;
        }
        return true;
    }

    /// <summary>
    /// Builds the 48-byte AES-256 key used to encrypt the request-signature payload.
    /// The first 16 bytes are derived from the decoded nonce by indexing into <see cref="Key1"/>;
    /// the next 32 bytes are <see cref="Key1"/> (no — sorry: <see cref="Key2"/> — corrected) appended.
    /// </summary>
    /// <remarks>
    /// Per the public protocol: <c>key[i] = KEY_1[ord(nonce[i]) % 16]</c> for i in 0..15,
    /// then key += KEY_2. Total length = 16 + 16 = 32 bytes (AES-256).
    /// </remarks>
    public static byte[] DeriveSignatureKey(string decodedNonce, string key1)
    {
        if (decodedNonce.Length < 16) throw new ArgumentException("nonce must be at least 16 chars", nameof(decodedNonce));
        var sb = new StringBuilder(32);
        for (int i = 0; i < 16; i++)
        {
            int idx = decodedNonce[i] % 16;
            sb.Append(key1[idx]);
        }
        sb.Append(Key2);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Computes the Base64-encoded request signature: AES-CBC-encrypt the decoded nonce
    /// using the derived key (first 16 bytes are also the IV), PKCS#7 padding.
    /// This is the value that goes into the <c>Authorization: FUS signature="..."</c> header.
    /// The KEY_1 used here MUST match the one that decoded the nonce.
    /// </summary>
    public static string ComputeAuthSignature(string decodedNonce, string key1)
    {
        var keyBytes = DeriveSignatureKey(decodedNonce, key1);
        var ivBytes = keyBytes.AsSpan(0, 16).ToArray();
        var plain = Encoding.UTF8.GetBytes(decodedNonce);

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = keyBytes;
        aes.IV = ivBytes;

        using var encryptor = aes.CreateEncryptor();
        var encrypted = encryptor.TransformFinalBlock(plain, 0, plain.Length);
        return Convert.ToBase64String(encrypted);
    }

    /// <summary>
    /// LOGIC_CHECK for the BinaryInform request body and for the V4 firmware key derivation.
    /// For each char c in <paramref name="nonce"/>, output[c] = input[c &amp; 0xF].
    /// </summary>
    public static string GetLogicCheck(string input, string nonce)
    {
        if (input.Length < 16) throw new ArgumentException("input too short", nameof(input));
        var sb = new StringBuilder(nonce.Length);
        foreach (var c in nonce)
            sb.Append(input[c & 0xF]);
        return sb.ToString();
    }

    /// <summary>
    /// V4 firmware decryption key: MD5(GetLogicCheck(LATEST_FW_VERSION, LOGIC_VALUE_FACTORY)).
    /// </summary>
    public static byte[] DeriveFirmwareKeyV4(string latestFwVersion, string logicValueFactory)
    {
        var deckey = GetLogicCheck(latestFwVersion, logicValueFactory);
        return MD5.HashData(Encoding.UTF8.GetBytes(deckey));
    }

    /// <summary>
    /// V2 (legacy) firmware decryption key: MD5("REGION:MODEL:VERSION").
    /// Used for older firmware downloaded via the legacy endpoint.
    /// </summary>
    public static byte[] DeriveFirmwareKeyV2(string region, string model, string version)
    {
        var deckey = $"{region}:{model}:{version}";
        return MD5.HashData(Encoding.UTF8.GetBytes(deckey));
    }
}
