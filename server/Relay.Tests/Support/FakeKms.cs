using System.Security.Cryptography;
using Relay.Domain.Security;

namespace Relay.Tests.Support;

/// <summary>
/// A fake KMS with no crypto dependencies: it "wraps" a data key by XORing it
/// with a fixed mask (its own inverse). Enough to exercise the envelope round
/// trip and rotation without touching a real key service.
/// </summary>
public sealed class FakeKms : IKeyManagementService
{
    private static readonly byte[] Mask = Enumerable.Range(0, 64).Select(i => (byte)(i * 7 + 13)).ToArray();

    public int GenerateCalls { get; private set; }

    public DataKey GenerateDataKey()
    {
        GenerateCalls++;
        var dek = RandomNumberGenerator.GetBytes(32);
        return new DataKey(dek, Transform(dek));
    }

    public byte[] UnwrapDataKey(byte[] wrapped) => Transform(wrapped);

    private static byte[] Transform(byte[] data)
    {
        var result = new byte[data.Length];
        for (var i = 0; i < data.Length; i++) result[i] = (byte)(data[i] ^ Mask[i % Mask.Length]);
        return result;
    }
}
