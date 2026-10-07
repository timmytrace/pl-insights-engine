using System.Text.Json;
using System.Text.Json.Serialization;

namespace Studio.Engine;

/// <summary>One wire format for files, the API and the overlay: camelCase, snake_case enums, no nulls.</summary>
public static class StudioJson
{
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions());

    public static JsonSerializerOptions Configure(JsonSerializerOptions o)
    {
        o.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return o;
    }
}
