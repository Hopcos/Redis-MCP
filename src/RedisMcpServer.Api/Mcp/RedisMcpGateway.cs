using System.Collections.ObjectModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RedisMcpServer.Core.Environment;
using RedisMcpServer.Core.Json;
using RedisMcpServer.Core.Tools;

namespace RedisMcpServer.Api.Mcp;

/// <summary>
/// Bridge between the protocol-neutral tool framework (Core/Tools) and the MCP SDK.
/// Supplies the <c>tools/list</c> and <c>tools/call</c> handlers with a single, shared
/// invocation path (the same one the REST API uses), preserving real error messages
/// (isError = true) and injecting the environment ToolDescription into every description.
/// </summary>
public sealed class RedisMcpGateway
{
    private readonly IToolRegistry _registry;
    private readonly IToolInvoker _invoker;
    private readonly IRedisEnvironmentResolver _resolver;
    private readonly Lazy<ListToolsResult> _toolsList;

    public RedisMcpGateway(IToolRegistry registry, IToolInvoker invoker, IRedisEnvironmentResolver resolver)
    {
        _registry = registry;
        _invoker = invoker;
        _resolver = resolver;
        ServerInfo = new Implementation
        {
            Name = "redis-mcp-server",
            Version = typeof(RedisMcpGateway).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
        };
        Instructions = InstructionsBuilder.Build(ServerInfo.Name, ServerInfo.Version ?? "1.0.0", registry, resolver);
        _toolsList = new Lazy<ListToolsResult>(BuildToolsList, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Server identity advertised during MCP initialization.</summary>
    public Implementation ServerInfo { get; }

    /// <summary>Global usage guide served as the MCP "instructions" (also covers every tool).</summary>
    public string Instructions { get; }

    /// <summary>Enriched tool description: base text + the environment block with each ToolDescription.</summary>
    public string EnrichDescription(ITool tool)
        => tool.Description + _resolver.BuildToolDescriptionSuffix();

    public ListToolsResult ListTools() => _toolsList.Value;

    public async ValueTask<CallToolResult> CallToolAsync(RequestContext<CallToolRequestParams> request, CancellationToken ct)
    {
        var arguments = request.Params.Arguments ?? new Dictionary<string, JsonElement>();
        return await CallToolAsync(request.Params.Name, new ReadOnlyDictionary<string, JsonElement>(arguments), ct).ConfigureAwait(false);
    }

    /// <summary>Protocol-neutral invocation used by the MCP handler and by tests.</summary>
    public async ValueTask<CallToolResult> CallToolAsync(string name, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken ct)
    {
        var environment = arguments.TryGetValue("environment", out var envElement)
            ? envElement.ValueKind == JsonValueKind.String ? envElement.GetString() : null
            : null;

        var result = await _invoker.InvokeAsync(name, environment, arguments, ct).ConfigureAwait(false);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = result.Text }],
            IsError = result.IsError,
        };
    }

    private ListToolsResult BuildToolsList()
    {
        var tools = _registry.Tools.Select(tool =>
        {
            var schema = ToolSchemaBuilder.Build(tool, _resolver);
            return new Tool
            {
                Name = tool.Name,
                Description = EnrichDescription(tool),
                InputSchema = JsonSerializer.SerializeToElement(schema, JsonDefaults.Options),
            };
        }).ToList();

        return new ListToolsResult { Tools = tools };
    }
}
