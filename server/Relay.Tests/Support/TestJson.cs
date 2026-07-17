using System.Text.Json;
using System.Text.Json.Serialization;

namespace Relay.Tests.Support;

/// <summary>JSON options mirroring the API (camelCase + string enums) for test deserialization.</summary>
public static class TestJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
