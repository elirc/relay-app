using Relay.Domain.Validation;

namespace Relay.Tests;

public sealed class JsonSchemaValidatorTests
{
    private const string SlackSchema =
        """{"type":"object","properties":{"channel":{"type":"string"}},"required":["channel"]}""";
    private const string DelaySchema =
        """{"type":"object","properties":{"seconds":{"type":"integer"}},"required":["seconds"]}""";

    [Fact]
    public void ValidConfig_HasNoErrors()
    {
        var errors = JsonSchemaValidator.Validate(SlackSchema, """{"channel":"#ops"}""");
        Assert.Empty(errors);
    }

    [Fact]
    public void MissingRequired_ReportsError()
    {
        var errors = JsonSchemaValidator.Validate(SlackSchema, "{}");
        Assert.Contains(errors, e => e.Contains("channel"));
    }

    [Fact]
    public void WrongType_ReportsError()
    {
        var errors = JsonSchemaValidator.Validate(DelaySchema, """{"seconds":"soon"}""");
        Assert.Contains(errors, e => e.Contains("seconds"));
    }

    [Fact]
    public void IntegerAcceptsWholeNumber_ButNotDecimal()
    {
        Assert.Empty(JsonSchemaValidator.Validate(DelaySchema, """{"seconds":30}"""));
        Assert.NotEmpty(JsonSchemaValidator.Validate(DelaySchema, """{"seconds":1.5}"""));
    }

    [Fact]
    public void NonObjectConfig_ReportsError()
    {
        var errors = JsonSchemaValidator.Validate(SlackSchema, "\"just a string\"");
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ExtraProperties_AreAllowed()
    {
        var errors = JsonSchemaValidator.Validate(SlackSchema, """{"channel":"#ops","extra":true}""");
        Assert.Empty(errors);
    }

    [Fact]
    public void MalformedSchema_ImposesNoConstraints()
    {
        var errors = JsonSchemaValidator.Validate("not a schema", """{"anything":1}""");
        Assert.Empty(errors);
    }

    [Fact]
    public void MalformedConfig_ReportsError()
    {
        var errors = JsonSchemaValidator.Validate(SlackSchema, "{ not json");
        Assert.NotEmpty(errors);
    }
}
