using Relay.Infrastructure.Security;
using Relay.Tests.Support;

namespace Relay.Tests;

public sealed class SecretProtectorTests
{
    private const string Secret = """{"apiKey":"sk-live-super-secret"}""";

    [Fact]
    public void Protect_ThenReveal_RoundTrips()
    {
        var protector = new EnvelopeSecretProtector(new FakeKms());
        var envelope = protector.Protect(Secret);
        Assert.Equal(Secret, protector.Reveal(envelope));
    }

    [Fact]
    public void Envelope_DoesNotContainPlaintext()
    {
        var protector = new EnvelopeSecretProtector(new FakeKms());
        var envelope = protector.Protect(Secret);
        Assert.DoesNotContain("sk-live-super-secret", envelope);
        Assert.DoesNotContain("apiKey", envelope);
    }

    [Fact]
    public void Protect_IsNonDeterministic()
    {
        var protector = new EnvelopeSecretProtector(new FakeKms());
        Assert.NotEqual(protector.Protect(Secret), protector.Protect(Secret));
    }

    [Fact]
    public void Rotate_ChangesEnvelope_KeepsPlaintext_UsesNewDataKey()
    {
        var kms = new FakeKms();
        var protector = new EnvelopeSecretProtector(kms);
        var original = protector.Protect(Secret);
        var before = kms.GenerateCalls;

        var rotated = protector.Rotate(original);

        Assert.NotEqual(original, rotated);
        Assert.Equal(Secret, protector.Reveal(rotated));
        Assert.True(kms.GenerateCalls > before); // a fresh data key was minted
    }

    [Fact]
    public void LocalKms_And_Protector_RoundTrip()
    {
        // The production path: AES-GCM data keys wrapped under a master key.
        var kms = new LocalKeyManagementService(new byte[32]);
        var protector = new EnvelopeSecretProtector(kms);
        var envelope = protector.Protect(Secret);
        Assert.Equal(Secret, protector.Reveal(envelope));
    }
}
