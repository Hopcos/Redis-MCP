using System.Text.Json.Nodes;
using RedisMcpServer.Core.Environment;

namespace RedisMcpServer.Core.Tools;

/// <summary>
/// Builds the JSON input schema for a tool (MCP <c>inputSchema</c> and REST documentation).
/// The "environment" parameter is injected automatically with the available environment names.
/// </summary>
public static class ToolSchemaBuilder
{
    public static JsonObject Build(ITool tool, IRedisEnvironmentResolver resolver)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        // environment is always offered, never required.
        properties["environment"] = new JsonObject
        {
            ["type"] = "string",
            ["description"] = resolver.BuildEnvironmentParameterDescription(),
        };

        foreach (var parameter in tool.Parameters)
        {
            if (string.Equals(parameter.Name, "environment", StringComparison.OrdinalIgnoreCase))
            {
                continue; // injected above with enriched documentation
            }

            properties[parameter.Name] = Describe(parameter);
            if (parameter.IsRequired)
            {
                required.Add(parameter.Name);
            }
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
        };
        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        return schema;
    }

    private static JsonObject Describe(ToolParameter parameter)
    {
        var node = new JsonObject { ["type"] = SchemaType(parameter) };

        if (!string.IsNullOrEmpty(parameter.Description))
        {
            node["description"] = parameter.Description;
        }

        if (parameter.AllowedValues is { Length: > 0 })
        {
            node["enum"] = new JsonArray(parameter.AllowedValues.Select(v => (JsonNode)v).ToArray());
        }

        // Only string-like kinds are parameterized; ints/bools carry their values directly.
        return node;
    }

    private static JsonNode SchemaType(ToolParameter parameter)
        => parameter.Kind switch
        {
            ToolParameterKind.String => "string",
            ToolParameterKind.Integer => "integer",
            ToolParameterKind.Number => "number",
            ToolParameterKind.Boolean => "boolean",
            ToolParameterKind.StringArray => new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
            _ => "string",
        };
}
