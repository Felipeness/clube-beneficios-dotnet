using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Clube.Adapters.Http;

internal sealed class PspOptions
{
    public const string Section = "Psp";
    public const int MinimumSecretLength = 32;

    public string WebhookSecret { get; set; } = "";
}

// Stripe-style signature: hex HMAC-SHA256 of "{timestamp}.{raw body}". The timestamp is inside what is signed,
// so a captured delivery cannot be replayed once it falls outside the tolerance.
internal sealed class PixSignature(IOptions<PspOptions> options, TimeProvider clock)
{
    private const int SignatureHexLength = HMACSHA256.HashSizeInBytes * 2;
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    private readonly byte[] secret = Encoding.UTF8.GetBytes(options.Value.WebhookSecret);

    // Answers only yes or no: telling the caller which check failed would help someone forging deliveries.
    public bool IsAuthentic(string? timestamp, string? signature, byte[] body)
    {
        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out long unixSeconds)) return false;
        if (!IsFresh(unixSeconds)) return false;
        if (!TryDecode(signature, out byte[] given)) return false;

        return CryptographicOperations.FixedTimeEquals(Sign(timestamp, body), given);
    }

    // Rejects the future too: a far-ahead timestamp would otherwise stay replayable for as long as it is ahead.
    private bool IsFresh(long unixSeconds)
    {
        long now = clock.GetUtcNow().ToUnixTimeSeconds();
        return Math.Abs(now - unixSeconds) <= Tolerance.TotalSeconds;
    }

    private static bool TryDecode(string? hex, out byte[] bytes)
    {
        bytes = new byte[HMACSHA256.HashSizeInBytes];
        return hex is { Length: SignatureHexLength } && Convert.FromHexString(hex, bytes, out _, out _) == OperationStatus.Done;
    }

    private byte[] Sign(string timestamp, byte[] body)
    {
        byte[] signed = [.. Encoding.UTF8.GetBytes($"{timestamp}."), .. body];
        return HMACSHA256.HashData(secret, signed);
    }
}
