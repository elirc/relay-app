using Relay.Domain.Security;

namespace Relay.Tests;

public sealed class WebhookSignatureTests
{
    private const string Secret = "shhh-super-secret";
    private const string Timestamp = "1717243200";
    private const string Body = """{"email":"a@b.c"}""";

    [Fact]
    public void Compute_IsDeterministic()
    {
        Assert.Equal(
            WebhookSignature.Compute(Secret, Timestamp, Body),
            WebhookSignature.Compute(Secret, Timestamp, Body));
    }

    [Fact]
    public void Verify_Accepts_AMatchingSignature()
    {
        var sig = WebhookSignature.Compute(Secret, Timestamp, Body);
        Assert.True(WebhookSignature.Verify(Secret, Timestamp, Body, sig));
    }

    [Fact]
    public void Verify_Rejects_TamperedBody()
    {
        var sig = WebhookSignature.Compute(Secret, Timestamp, Body);
        Assert.False(WebhookSignature.Verify(Secret, Timestamp, """{"email":"evil@b.c"}""", sig));
    }

    [Fact]
    public void Verify_Rejects_WrongSecret()
    {
        var sig = WebhookSignature.Compute(Secret, Timestamp, Body);
        Assert.False(WebhookSignature.Verify("other-secret", Timestamp, Body, sig));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-hex-zzzz")]
    public void Verify_Rejects_MissingOrMalformedSignature(string? provided)
    {
        Assert.False(WebhookSignature.Verify(Secret, Timestamp, Body, provided));
    }
}
