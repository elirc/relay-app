using System.Security.Cryptography;
using Relay.Domain.Security;

namespace Relay.Infrastructure.Security;

/// <summary>
/// A local stand-in for a cloud KMS: it wraps data keys with AES-GCM under a
/// configured master key. No external calls; the master key comes from
/// configuration (<c>Secrets:MasterKey</c>, base64, 32 bytes).
/// </summary>
public sealed class LocalKeyManagementService : IKeyManagementService
{
    private const int DataKeySize = 32; // AES-256 data keys
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _masterKey;

    public LocalKeyManagementService(byte[] masterKey)
    {
        if (masterKey.Length is not (16 or 24 or 32))
            throw new ArgumentException("Master key must be 16, 24, or 32 bytes.", nameof(masterKey));
        _masterKey = masterKey;
    }

    public DataKey GenerateDataKey()
    {
        var plaintext = RandomNumberGenerator.GetBytes(DataKeySize);
        return new DataKey(plaintext, Wrap(plaintext));
    }

    public byte[] UnwrapDataKey(byte[] wrapped)
    {
        var nonce = wrapped.AsSpan(0, NonceSize);
        var tag = wrapped.AsSpan(NonceSize, TagSize);
        var cipher = wrapped.AsSpan(NonceSize + TagSize);
        var plaintext = new byte[cipher.Length];
        using var aes = new AesGcm(_masterKey, TagSize);
        aes.Decrypt(nonce, cipher, tag, plaintext);
        return plaintext;
    }

    private byte[] Wrap(byte[] dataKey)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[dataKey.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(_masterKey, TagSize);
        aes.Encrypt(nonce, dataKey, cipher, tag);

        var wrapped = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(wrapped, 0);
        tag.CopyTo(wrapped, NonceSize);
        cipher.CopyTo(wrapped, NonceSize + TagSize);
        return wrapped;
    }
}
