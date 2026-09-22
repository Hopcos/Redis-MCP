using System.Text;
using RedisMcpServer.Core.Environment;

namespace RedisMcpServer.Core.Tools;

/// <summary>
/// Builds the global MCP "Instructions" usage guide. It is served to clients as
/// <c>instructions</c> in the initialize result and includes:
/// <list type="bullet">
/// <item>an overview of the server and its read-only guarantees;</item>
/// <item>every configured environment, including each environment's ToolDescription text;</item>
/// <item>the shared "environment" parameter convention;</item>
/// <item>every tool with its parameters and a usage example.</item>
/// </list>
/// </summary>
public static class InstructionsBuilder
{
    public static string Build(
        string serverName,
        string serverVersion,
        IToolRegistry registry,
        IRedisEnvironmentResolver resolver)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {serverName} — Global Usage Guide (v{serverVersion})");
        sb.AppendLine();
        sb.AppendLine("This MCP server exposes **read-only** query capabilities over the configured Redis "
            + "instances. No tool creates, modifies or deletes data — this is enforced by design in the "
            + "current release and write capabilities may be added later behind the same tool framework.");
        sb.AppendLine();
        sb.AppendLine("## Environments");
        sb.AppendLine();
        foreach (var env in resolver.Environments)
        {
            sb.AppendLine($"- **{env.Name}** — {env.DisplayDescription}  ");
            sb.AppendLine($"  - endpoint: `{env.Host}:{env.Port}` (display only), default database: {env.DefaultDatabase}");
        }

        sb.AppendLine();
        sb.AppendLine("## Conventions");
        sb.AppendLine();
        sb.AppendLine("- **environment parameter**: every tool accepts an optional `environment` argument "
            + $"selecting one of: {string.Join(", ", resolver.Environments.Select(e => e.Name))}. "
            + $"When omitted, the default environment **{resolver.Default.Name}** is used.");
        sb.AppendLine("- **result format**: every tool returns a JSON text payload. Errors are reported with "
            + "`isError: true` and a human-readable message (Redis error codes such as WRONGTYPE, WRONGPASS, "
            + "or timeout are preserved).");
        sb.AppendLine("- **keyspace scans**: use `scan_keys` (SCAN, non-blocking) instead of key enumeration "
            + "over huge keyspaces; listing is always cursor-based and bounded by the `count` parameter.");
        sb.AppendLine("- **value reads**: `get_key` auto-detects and decodes the value by Redis type and caps "
            + "large payloads with the `limit`/`maxValueChars` parameters. Binary data that is not valid UTF-8 "
            + "is returned as `base64:...`.");
        sb.AppendLine("- **timeouts**: reads fail fast with a descriptive timeout message; the server never "
            + "retries mutating commands (there are none) and never blocks the thread pool (all Redis access "
            + "is asynchronous).");
        sb.AppendLine();
        sb.AppendLine("## Tools");
        sb.AppendLine();

        ITool? lastTool = null;
        foreach (var tool in registry.Tools)
        {
            if (lastTool is null || !string.Equals(lastTool.Group, tool.Group, StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"### Group: {tool.Group}");
                sb.AppendLine();
            }

            lastTool = tool;
            sb.AppendLine($"#### `{tool.Name}` — {tool.Description.ReplaceLineEndings(" ")}");
            sb.AppendLine();
            sb.AppendLine("Parameters:");
            sb.AppendLine();
            sb.AppendLine("| Name | Type | Required | Description |");
            sb.AppendLine("|---|---|---|---|");
            sb.AppendLine($"| environment | string | no | {resolver.BuildEnvironmentParameterDescription()} |");
            foreach (var p in tool.Parameters)
            {
                if (string.Equals(p.Name, "environment", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var allowed = p.AllowedValues is { Length: > 0 } ? $" (one of: {string.Join(", ", p.AllowedValues)})" : string.Empty;
                sb.AppendLine($"| {p.Name} | {p.Kind.ToString().ToLowerInvariant()} | {(p.IsRequired ? "yes" : "no")} | {p.Description}{allowed} |");
            }

            sb.AppendLine();
            sb.AppendLine($"Example call: {BuildExample(tool, resolver)}");
            sb.AppendLine();
        }

        sb.AppendLine("## Limits and notes");
        sb.AppendLine();
        sb.AppendLine("- Server-side commands such as INFO/CONFIG GET/SLOWLOG/LATENCY target the first endpoint "
            + "of the selected environment (in cluster mode, one node).");
        sb.AppendLine("- `pubsub_*` tools only inspect the channel namespace; they do not publish messages.");
        sb.AppendLine("- The server runs over HTTP transport; see the project README for endpoint details "
            + "(`/mcp` streamable HTTP + legacy SSE, and the REST API under `/api`).");
        return sb.ToString();
    }

    private static string BuildExample(ITool tool, IRedisEnvironmentResolver resolver)
    {
        var example = new System.Text.StringBuilder();
        example.Append('{');
        var first = true;
        foreach (var p in tool.Parameters)
        {
            if (string.Equals(p.Name, "environment", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!first)
            {
                example.Append(", ");
            }

            first = false;
            var sample = p.Kind switch
            {
                ToolParameterKind.String => "string",
                ToolParameterKind.Integer => "0",
                ToolParameterKind.Number => "1.0",
                ToolParameterKind.Boolean => "true",
                ToolParameterKind.StringArray => "[\"a\"]",
                _ => "\"...\"",
            };
            example.Append('"').Append(p.Name).Append("\": ").Append(sample);
        }

        if (first)
        {
            example.Append('"').Append("environment").Append("\": \"").Append(resolver.Default.Name).Append('"');
        }

        example.Append('}');
        return example.ToString();
    }
}
