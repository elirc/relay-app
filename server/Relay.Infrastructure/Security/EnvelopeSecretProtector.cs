using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Relay.Domain.Security;

namespace Relay.Infrastructure.Security;

/// <summary>
/// Envelope encryption over the <see cref="IKeyManagementService"/> port: each
/// secret is sealed with AES-GCM under a fresh data key, and the wrapped data key
/// travels in the envelope. Rotation re-seals under a brand-new data key.
/// </summary>
public sealed class EnvelopeSecretProtector : ISecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IKeyManagementService _kms;

    public EnvelopeSecretProtector(IKeyManagementService kms) => _kms = kms;

    public string Protect(string plaintext)
    {
        var dataKey = _kms.GenerateDataKey();
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(dataKey.Plaintext, TagSize))
        {
            aes.Encrypt(nonce, plainBytes, cipher, tag);
        }
        CryptographicOperations.ZeroMemory(dataKey.Plaintext);

        var envelope = new Envelope(
            1,
            Convert.ToBase64String(dataKey.Wrapped),
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag),
            Convert.ToBase64String(cipher));
        return JsonSerializer.Serialize(envelope);
    }

    public string Reveal(string envelope)
    {
        var e = JsonSerializer.Deserialize<Envelope>(envelope)
            ?? throw new FormatException("Malformed secret envelope.");

        var dataKey = _kms.UnwrapDataKey(Convert.FromBase64String(e.WrappedKey));
        var cipher = Convert.FromBase64String(e.Cipher);
        var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(dataKey, TagSize);
            aes.Decrypt(
                Convert.FromBase64String(e.Nonce),
                cipher,
                Convert.FromBase64String(e.Tag),
                plain);
            return Encoding.UTF8.GetString(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public string Rotate(string envelope) => Protect(Reveal(envelope));

    private sealed record Envelope(int V, string WrappedKey, string Nonce, string Tag, string Cipher);
}
