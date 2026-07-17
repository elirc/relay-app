using System.Text.Json;

namespace Relay.Domain.Validation;

/// <summary>
/// A deliberately small JSON Schema validator covering the subset the connector
/// catalog uses: an object schema with typed <c>properties</c> and a
/// <c>required</c> list. It checks that the config is a JSON object, that every
/// required property is present and non-null, and that any present property
/// declared in the schema matches its declared type. Unknown properties are
/// allowed. A malformed schema is treated as "no constraints".
/// </summary>
public static class JsonSchemaValidator
{
    /// <summary>Validates <paramref name="configJson"/> against <paramref name="schemaJson"/>.</summary>
    /// <returns>An ordered list of human-readable errors; empty when valid.</returns>
    public static IReadOnlyList<string> Validate(string schemaJson, string? configJson)
    {
        var errors = new List<string>();

        JsonElement schema;
        try
        {
            using var schemaDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(schemaJson) ? "{}" : schemaJson);
            schema = schemaDoc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return errors; // unusable schema → impose no constraints
        }

        if (schema.ValueKind != JsonValueKind.Object) return errors;

        JsonElement config;
        try
        {
            using var configDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(configJson) ? "{}" : configJson);
            config = configDoc.RootElement.Clone();
        }
        catch (JsonException)
        {
            errors.Add("Config is not valid JSON.");
            return errors;
        }

        // Top-level object schemas require an object config.
        if (SchemaType(schema) is "object" or null && config.ValueKind != JsonValueKind.Object)
        {
            errors.Add("Config must be a JSON object.");
            return errors;
        }

        var hasProperties = schema.TryGetProperty("properties", out var properties)
            && properties.ValueKind == JsonValueKind.Object;

        // required: every listed property must be present and non-null.
        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in required.EnumerateArray())
            {
                var name = item.GetString();
                if (name is null) continue;
                if (!config.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
                {
                    errors.Add($"Missing required property '{name}'.");
                }
            }
        }

        // Type-check present properties that the schema declares.
        if (hasProperties)
        {
            foreach (var prop in properties.EnumerateObject())
            {
                if (!config.TryGetProperty(prop.Name, out var value) || value.ValueKind == JsonValueKind.Null)
                    continue;
                var declared = SchemaType(prop.Value);
                if (declared is not null && !MatchesType(declared, value))
                {
                    errors.Add($"Property '{prop.Name}' must be of type '{declared}'.");
                }
            }
        }

        return errors;
    }

    private static string? SchemaType(JsonElement schema) =>
        schema.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("type", out var type)
            && type.ValueKind == JsonValueKind.String
            ? type.GetString()
            : null;

    private static bool MatchesType(string declared, JsonElement value) => declared switch
    {
        "string" => value.ValueKind == JsonValueKind.String,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        _ => true, // unknown declared type → don't block
    };
}
