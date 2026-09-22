using RedisMcpServer.Api.Mcp;
using RedisMcpServer.Core.Environment;
using RedisMcpServer.Core.Redis;
using RedisMcpServer.Core.Tools;

namespace RedisMcpServer.Api.Api;

/// <summary>
/// REST surface for every MCP tool: listing/debugging (GET) and invocation (POST).
/// Both MCP and REST call the very same <see cref="IToolInvoker"/>, so behavior is identical.
/// </summary>
public static class ToolApiEndpoints
{
    public static IEndpointRouteBuilder MapToolApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api");

        group.MapGet("/health", async (IRedisConnectionProvider provider, CancellationToken ct) =>
        {
            var statuses = await provider.GetStatusSnapshotAsync(ct);
            return Results.Ok(new
            {
                status = "ok",
                environments = statuses.Select(s => new { s.EnvironmentName, s.IsConnected, s.Status }),
            });
        }).WithSummary("Server health and connection status of every configured environment.");

        group.MapGet("/environments", (IRedisEnvironmentResolver resolver) =>
        {
            var environments = resolver.Environments.Select(e => new
            {
                name = e.Name,
                description = e.DisplayDescription,
                toolDescription = e.ToolDescription,
                host = e.Host,
                port = e.Port,
                defaultDatabase = e.DefaultDatabase,
                hasPassword = e.HasPassword,
                isDefault = string.Equals(e.Name, resolver.Default.Name, StringComparison.OrdinalIgnoreCase),
            });
            return Results.Ok(new { defaultEnvironment = resolver.Default.Name, environments });
        }).WithSummary("List the configured Redis environments.");

        group.MapGet("/tools", (IToolRegistry registry, IRedisEnvironmentResolver resolver, RedisMcpGateway gateway) =>
        {
            var tools = registry.Tools.Select(t => ToDocument(t, resolver, gateway));
            return Results.Ok(new { count = registry.Tools.Count, tools });
        }).WithSummary("List every available tool with its parameters and JSON schema.");

        group.MapGet("/tools/{name}", (string name, IToolRegistry registry, IRedisEnvironmentResolver resolver, RedisMcpGateway gateway) =>
        {
            if (!registry.TryGet(name, out var tool))
            {
                return Results.NotFound($"Tool '{name}' was not found.");
            }

            return Results.Ok(ToDocument(tool, resolver, gateway));
        }).WithSummary("Get the documentation of one tool.");

        group.MapPost("/tools/{name}/invoke", async (
            string name,
            ToolInvokeRequest request,
            IToolRegistry registry,
            IToolInvoker invoker,
            CancellationToken ct) =>
        {
            if (!registry.TryGet(name, out _))
            {
                return Results.NotFound($"Tool '{name}' was not found.");
            }

            var arguments = request.ToArgumentDictionary();
            var result = await invoker.InvokeAsync(name, request.Environment, arguments, ct);
            return Results.Ok(result);
        }).WithSummary("Invoke one tool. Mirror of the MCP tools/call handler.");

        return endpoints;
    }

    /// <summary>Omit non-JSON-safe default values (NaN/±Infinity cannot be written as JSON numbers).</summary>
    private static object? ToJsonValue(object? value)
        => value is double d && (double.IsNaN(d) || double.IsInfinity(d)) ? null : value;

    private static object ToDocument(ITool tool, IRedisEnvironmentResolver resolver, RedisMcpGateway gateway)
    {
        var parameters = tool.Parameters
            .Where(p => !string.Equals(p.Name, "environment", StringComparison.OrdinalIgnoreCase))
            .Select(p => new
            {
                name = p.Name,
                type = p.Kind.ToString().ToLowerInvariant(),
                description = p.Description,
                required = p.IsRequired,
                defaultValue = ToJsonValue(p.DefaultValue),
                allowedValues = p.AllowedValues,
            });

        var schema = ToolSchemaBuilder.Build(tool, resolver);
        return new
        {
            name = tool.Name,
            group = tool.Group,
            description = gateway.EnrichDescription(tool),
            parameters,
            inputSchema = schema,
        };
    }
}
