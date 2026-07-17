namespace Relay.Api.Security;

/// <summary>
/// JWT signing/validation settings bound from the "Jwt" configuration section.
/// The dev key in appsettings is for local use only.
/// </summary>
public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "relay";
    public string Audience { get; set; } = "relay-client";
    public int ExpiryMinutes { get; set; } = 120;
}
