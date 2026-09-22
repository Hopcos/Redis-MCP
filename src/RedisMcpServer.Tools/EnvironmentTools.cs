using System.ComponentModel;
using RedisMcpServer.Core.Environment;
using RedisMcpServer.Core.Redis;
using RedisMcpServer.Core.Tools;

namespace RedisMcpServer.Tools;

/// <summary>Environment &amp; connectivity tools: listing environments and pinging Redis.</summary>
[McpToolGroup("Environment")]
public sealed class EnvironmentTools(IRedisConnectionProvider redis, IRedisEnvironmentResolver resolver) : RedisToolBase(redis)
{
    [McpTool(
        Name = "list_environments",
        Description = "List the Redis environments configured in appsettings (\"Redis\" section), "
            + "with their ToolDescription text, endpoints and default database. "
            + "列出配置的 Redis 环境。")]
    public Task<object?> ListEnvironmentsAsync(CancellationToken ct = default)
        => RunAsync(null, (ctx, _) => ValueTask.FromResult<object?>(new
        {
            defaultEnvironment = resolver.Default.Name,
            environments = resolver.Environments.Select(e => new
            {
                name = e.Name,
                description = e.DisplayDescription,
                toolDescription = e.ToolDescription,
                host = e.Host,
                port = e.Port,
                defaultDatabase = e.DefaultDatabase,
                hasPassword = e.HasPassword,
                isDefault = string.Equals(e.Name, resolver.Default.Name, StringComparison.OrdinalIgnoreCase),
            }),
            endpointNote = "Endpoints are displayed for identification only; connection strings are never exposed.",
        }), ct);

    [McpTool(
        Name = "ping",
        Description = "Ping the selected Redis instance and report round-trip latency and server version. "
            + "测试 Redis 连接与延迟。")]
    public async Task<object?> PingAsync(
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var latency = await ctx.Database.PingAsync().ConfigureAwait(false);
            string? version = null;
            try
            {
                var info = await ctx.Server.ExecuteReadAsync("INFO", "server").ConfigureAwait(false);
                version = ParseInfo(info.ToString() ?? string.Empty).GetValueOrDefault("redis_version");
            }
            catch
            {
                // version is best-effort
            }

            return new
            {
                environment = ctx.EnvironmentName,
                pong = true,
                latencyMs = latency.TotalMilliseconds,
                serverVersion = version,
            };
        }, ct);

    private static Dictionary<string, string> ParseInfo(string raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = line.IndexOf(':');
            if (idx > 0)
            {
                result[line[..idx].Trim()] = line[(idx + 1)..].Trim();
            }
        }

        return result;
    }
}
