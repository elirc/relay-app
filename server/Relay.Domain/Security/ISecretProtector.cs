namespace Relay.Domain.Security;

/// <summary>
/// Protects connection secrets with envelope encryption: each secret is sealed
/// under its own data key, which is wrapped by the key port. Secrets are
/// write-only from the API's perspective — <see cref="Reveal"/> exists only for
/// internal use (e.g. rotation), never to echo values back to clients.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Seals plaintext into a self-describing envelope string.</summary>
    string Protect(string plaintext);

    /// <summary>Opens an envelope back to plaintext.</summary>
    string Reveal(string envelope);

    /// <summary>Re-seals an envelope under a brand-new data key (key rotation).</summary>
    string Rotate(string envelope);
}
