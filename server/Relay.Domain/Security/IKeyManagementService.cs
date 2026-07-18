namespace Relay.Domain.Security;

/// <summary>A freshly generated data key: its plaintext and a wrapped (encrypted) form.</summary>
public sealed record DataKey(byte[] Plaintext, byte[] Wrapped);

/// <summary>
/// The key port for envelope encryption. It never sees secret payloads — only
/// data keys, which it wraps under a master key. The app supplies a local
/// implementation; tests supply a fake KMS.
/// </summary>
public interface IKeyManagementService
{
    /// <summary>Generates a new data key (plaintext + wrapped).</summary>
    DataKey GenerateDataKey();

    /// <summary>Unwraps a previously wrapped data key back to plaintext.</summary>
    byte[] UnwrapDataKey(byte[] wrapped);
}
