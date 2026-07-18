using System.Security.Cryptography;
using System.Text;

namespace Relay.Domain.Security;

/// <summary>
/// HMAC-SHA256 signing for inbound webhooks. The signature is computed over
/// <c>{timestamp}.{body}</c> so a captured request can't be replayed with a
/// different body, and callers include the timestamp for a freshness window.
/// </summary>
public static class WebhookSignature
{
    /// <summary>Computes the lowercase-hex signature for a timestamp + body.</summary>
    public static string Compute(string secret, string timestamp, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var mac = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    /// <summary>Constant-time check of a provided hex signature.</summary>
    public static bool Verify(string secret, string timestamp, string body, string? providedHex)
    {
        if (string.IsNullOrWhiteSpace(providedHex)) return false;

        var expected = Compute(secret, timestamp, body);
        byte[] expectedBytes, providedBytes;
        try
        {
            expectedBytes = Convert.FromHexString(expected);
            providedBytes = Convert.FromHexString(providedHex);
        }
        catch (FormatException)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}
