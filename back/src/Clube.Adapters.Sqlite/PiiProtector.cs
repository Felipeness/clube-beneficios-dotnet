using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Clube.Adapters.Sqlite;

internal sealed class PiiOptions
{
    public const string Section = "Pii";
    public const int KeyBytes = 32;
    public const string Invalid = "Pii:EncryptionKey and Pii:IndexKey must be two different base64 keys of 32 bytes each.";

    public string EncryptionKey { get; set; } = "";
    public string IndexKey { get; set; } = "";

    // Two different keys: an index key leaking must not also open the ciphertext, and vice versa.
    public bool AreValid() =>
        TryDecode(EncryptionKey, out byte[] encryption) && TryDecode(IndexKey, out byte[] index) && !encryption.SequenceEqual(index);

    public static byte[] Decode(string base64Key) =>
        TryDecode(base64Key, out byte[] key) ? key : throw new InvalidOperationException(Invalid);

    private static bool TryDecode(string base64Key, out byte[] key)
    {
        key = new byte[KeyBytes];
        return Convert.TryFromBase64String(base64Key, key, out int written) && written == KeyBytes;
    }
}

// AES-256-GCM so a database dump holds no CPF, plus an HMAC blind index so uniqueness still has a deterministic column to
// lean on. Stored as "{version}:{base64(nonce|ciphertext|tag)}": the version names the key, which is what rotation needs.
internal sealed class PiiProtector(byte[] encryptionKey, byte[] indexKey)
{
    private const string VersionPrefix = "v1:";
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    public static PiiProtector From(IOptions<PiiOptions> options) =>
        new(PiiOptions.Decode(options.Value.EncryptionKey), PiiOptions.Decode(options.Value.IndexKey));

    // A fresh random nonce per value: the same CPF never encrypts to the same text twice, so equal rows are not visible.
    public string Encrypt(string plaintext)
    {
        byte[] plain = Encoding.UTF8.GetBytes(plaintext);
        byte[] payload = new byte[NonceBytes + plain.Length + TagBytes];
        Span<byte> nonce = payload.AsSpan(0, NonceBytes);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(encryptionKey, TagBytes);
        aes.Encrypt(nonce, plain, payload.AsSpan(NonceBytes, plain.Length), payload.AsSpan(NonceBytes + plain.Length));
        return VersionPrefix + Convert.ToBase64String(payload);
    }

    // Throws instead of returning a value: GCM authenticates, so a tampered or foreign value is a system fault, never data.
    public string Decrypt(string stored)
    {
        byte[] payload = PayloadOf(stored);
        int cipherLength = payload.Length - NonceBytes - TagBytes;
        byte[] plain = new byte[cipherLength];
        using var aes = new AesGcm(encryptionKey, TagBytes);
        aes.Decrypt(payload.AsSpan(0, NonceBytes), payload.AsSpan(NonceBytes, cipherLength), payload.AsSpan(NonceBytes + cipherLength), plain);
        return Encoding.UTF8.GetString(plain);
    }

    public string BlindIndex(string plaintext) =>
        Convert.ToHexString(HMACSHA256.HashData(indexKey, Encoding.UTF8.GetBytes(plaintext)));

    private static byte[] PayloadOf(string stored)
    {
        if (!stored.StartsWith(VersionPrefix, StringComparison.Ordinal)) throw new CryptographicException("Stored PII has an unknown key version.");
        byte[] payload = Convert.FromBase64String(stored[VersionPrefix.Length..]);
        return payload.Length >= NonceBytes + TagBytes ? payload : throw new CryptographicException("Stored PII is truncated.");
    }
}
