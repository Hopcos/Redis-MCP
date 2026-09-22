using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using RedisMcpServer.Api.Mcp;
using RedisMcpServer.Core.Configuration;
using RedisMcpServer.Core.Environment;
using RedisMcpServer.Core.Tools;
using RedisMcpServer.Tools;

namespace RedisMcpServer.Tests;

/// <summary>
/// Verifies the discovery pipeline: reflection scan -> registry -> schema -> instructions -> MCP gateway.
/// The tools assembly is scanned with a fake provider so no Redis connection is required.
/// </summary>
public class ToolCatalogTests
{
    private static readonly RedisEnvironmentResolver Resolver = BuildResolver();
    private static readonly FakeRedisProvider Provider = new(Resolver);
    private static readonly IToolRegistry Registry = BuildRegistry();

    private static RedisEnvironmentResolver BuildResolver()
        => new(new[]
        {
            new RedisEnvironmentOptions
            {
                Name = "Stage",
                ToolDescription = "Stage环境的Name参数值为Stage",
                Connection = "xxx.yyy.local:6379,allowAdmin=false,defaultDatabase=6,password=secret",
            },
            new RedisEnvironmentOptions
            {
                Name = "Local",
                ToolDescription = "本地开发环境",
                Connection = "localhost:6379,allowAdmin=false,defaultDatabase=0",
            },
        });

    private static IToolRegistry BuildRegistry()
    {
        var discovered = ToolDiscovery.Scan(typeof(EnvironmentTools).Assembly);
        var tools = ToolDiscovery.Build(discovered, CreateInstance);
        return new ToolRegistry(tools);

        object CreateInstance(Type type)
        {
            if (type == typeof(EnvironmentTools))
            {
                return new EnvironmentTools(Provider, Resolver);
            }

            return Activator.CreateInstance(type, Provider)!
                ?? throw new InvalidOperationException($"Cannot instantiate tool class {type.FullName}.");
        }
    }

    [Fact]
    public void DiscoversAFullCatalogOfReadOnlyTools()
    {
        Assert.True(Registry.Tools.Count >= 40, $"Expected >= 40 tools, got {Registry.Tools.Count}.");
        Assert.Contains(Registry.Tools, t => t.Name == "scan_keys");
        Assert.Contains(Registry.Tools, t => t.Name == "list_environments");
        Assert.Contains(Registry.Tools, t => t.Name == "string_get");
        Assert.Contains(Registry.Tools, t => t.Name == "zset_range");
        Assert.Contains(Registry.Tools, t => t.Name == "stream_range");
        Assert.Contains(Registry.Tools, t => t.Name == "geo_search");
        Assert.Contains(Registry.Tools, t => t.Name == "server_info");
        Assert.Contains(Registry.Tools, t => t.Name == "cluster_info");
        Assert.Contains(Registry.Tools, t => t.Name == "pubsub_channels");
    }

    [Fact]
    public void EveryToolHasANameAndDescription()
    {
        foreach (var tool in Registry.Tools)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Name), $"Tool under group '{tool.Group}' has no name.");
            Assert.False(string.IsNullOrWhiteSpace(tool.Description), $"Tool '{tool.Name}' has no description.");
            Assert.False(string.IsNullOrWhiteSpace(tool.Group), $"Tool '{tool.Name}' has no group.");
        }
    }

    [Fact]
    public void ToolNamesAreUniqueCaseInsensitively()
    {
        var groups = Registry.Tools.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase);
        Assert.All(groups, g => Assert.Single(g));
    }

    [Fact]
    public void EveryRedisBackedToolHasAnEnvironmentParameter()
    {
        foreach (var tool in Registry.Tools.Where(t => t.Name != "list_environments"))
        {
            Assert.Contains(tool.Parameters, p => p.Name == "environment");
        }
    }

    [Fact]
    public void SchemaIncludesEnvironmentParameterWithDescription()
    {
        var tool = Registry.GetRequired("string_get");
        var schema = ToolSchemaBuilder.Build(tool, Resolver);
        var environment = schema["properties"]?["environment"];

        Assert.NotNull(environment);
        Assert.Contains("Stage", environment!["description"]!.GetValue<string>());
        var required = ((JsonArray)schema["required"]!).Select(n => n!.GetValue<string>()).ToArray();
        Assert.Equal(new[] { "key" }, required);
    }

    [Fact]
    public void InstructionsContainEveryToolAndEveryToolDescription()
    {
        var instructions = InstructionsBuilder.Build("redis-tests", "1.0.0", Registry, Resolver);
        Assert.Contains("Stage环境的Name参数值为Stage", instructions);
        Assert.Contains("本地开发环境", instructions);
        foreach (var tool in Registry.Tools)
        {
            Assert.Contains(tool.Name, instructions);
        }
    }

    [Fact]
    public void MCPGatewayListsAllTools_WithEnrichedDescriptions()
    {
        var gateway = new RedisMcpGateway(Registry, new ToolInvoker(Registry), Resolver);
        var list = gateway.ListTools();

        Assert.Equal(Registry.Tools.Count, list.Tools.Count);
        foreach (var tool in list.Tools)
        {
            Assert.Contains("Stage环境的Name参数值为Stage", tool.Description);
            Assert.NotEqual(JsonValueKind.Undefined, tool.InputSchema.ValueKind);
        }
    }

    [Fact]
    public async Task MCPGatewayCall_ReturnsRealErrors_AndEnvironmentMetadata()
    {
        var gateway = new RedisMcpGateway(Registry, new ToolInvoker(Registry), Resolver);

        // Missing required parameter must be a structured isError result, not HTTP-level failure.
        var error = await gateway.CallToolAsync("string_get", new Dictionary<string, JsonElement>(), CancellationToken.None);
        Assert.True(error.IsError);
        Assert.Contains("Missing required parameter 'key'", ((ModelContextProtocol.Protocol.TextContentBlock)error.Content[0]).Text);

        // list_environments resolves without touching Redis and reports the ToolDescription.
        var ok = await gateway.CallToolAsync("list_environments", new Dictionary<string, JsonElement>(), CancellationToken.None);
        Assert.False(ok.IsError);
        Assert.Contains("Stage", ((ModelContextProtocol.Protocol.TextContentBlock)ok.Content[0]).Text);
    }
}
