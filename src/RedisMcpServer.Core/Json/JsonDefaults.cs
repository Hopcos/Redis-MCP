using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RedisMcpServer.Core.Json;

/// <summary>Shared JSON serialization settings for tool results and REST payloads.</summary>
public static class JsonDefaults
{
    /// <summary>
    /// Camel-case, tolerant of enums/records; used to render tool output to text.
    /// UnsafeRelaxedJsonEscaping keeps the text readable (no \u0027 escapes) — safe here
    /// because results are served over a trusted local API, not embedded in HTML.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>Serializes a tool result payload to a compact, human-readable JSON string.</summary>
    public static string ToText(object? data)
        => data is null ? "null" : JsonSerializer.Serialize(data, Options);
}
