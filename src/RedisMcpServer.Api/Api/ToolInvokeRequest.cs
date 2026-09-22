using System.Text.Json;
using RedisMcpServer.Core.Tools;

namespace RedisMcpServer.Api.Api;

/// <summary>
/// Request body for <c>POST /api/tools/&lt;name&gt;/invoke</c>:
/// optional <c>environment</c> (overrides the per-tool "environment" argument) and
/// optional <c>arguments</c> object with the tool parameters.
/// </summary>
public sealed class ToolInvokeRequest
{
    /// <summary>Redis environment name; when omitted the first configured environment is used.</summary>
    public string? Environment { get; set; }

    /// <summary>Tool arguments as a JSON object, e.g. {"key": "user:42"}.</summary>
    public JsonElement? Arguments { get; set; }
}

/// <summary>Converts the request body arguments into the dictionary consumed by <see cref="IToolInvoker"/>.</summary>
public static class ToolInvokeRequestExtensions
{
    public static IReadOnlyDictionary<string, JsonElement> ToArgumentDictionary(this ToolInvokeRequest request)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (request?.Arguments is { ValueKind: JsonValueKind.Object } arguments)
        {
            foreach (var property in arguments.EnumerateObject())
            {
                result[property.Name] = property.Value;
            }
        }

        return result;
    }
}
