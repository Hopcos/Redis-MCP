using System.ComponentModel;
using RedisMcpServer.Core.Json;
using RedisMcpServer.Core.Redis;
using RedisMcpServer.Core.Tools;
using StackExchange.Redis;

namespace RedisMcpServer.Tools.Server;

/// <summary>Cluster topology tools (CLUSTER INFO / NODES). Read-only inspection of one node.</summary>
[McpToolGroup("Cluster")]
public sealed class ClusterTools(IRedisConnectionProvider redis) : RedisToolBase(redis)
{
    [McpTool(
        Name = "cluster_info",
        Description = "Return cluster state from the selected node (CLUSTER INFO). 返回集群状态信息。")]
    public async Task<object?> InfoAsync(
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var raw = await ctx.Server.ExecuteReadAsync("CLUSTER", "INFO").ConfigureAwait(false);
            return new { cluster = ParseKeyValues(raw.ToString() ?? string.Empty) };
        }, ct);

    [McpTool(
        Name = "cluster_nodes",
        Description = "Return the cluster node table (CLUSTER NODES): id, address, flags, role, slots. "
            + "返回集群节点表。")]
    public async Task<object?> NodesAsync(
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var raw = await ctx.Server.ExecuteReadAsync("CLUSTER", "NODES").ConfigureAwait(false);
            var lines = (raw.ToString() ?? string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var nodes = lines.Select(line =>
            {
                var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return new
                {
                    id = fields.ElementAtOrDefault(0),
                    address = fields.ElementAtOrDefault(1),
                    flags = fields.ElementAtOrDefault(2),
                    master = fields.ElementAtOrDefault(3),
                    pingSent = fields.ElementAtOrDefault(4),
                    pongReceived = fields.ElementAtOrDefault(5),
                    configEpoch = fields.ElementAtOrDefault(6),
                    linkState = fields.ElementAtOrDefault(7),
                    slots = string.Join(",", fields.Skip(8)),
                };
            });
            return new { nodes };
        }, ct);

    private static Dictionary<string, string> ParseKeyValues(string raw)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = line.IndexOf(':');
            if (idx > 0)
            {
                dict[line[..idx].Trim()] = line[(idx + 1)..].Trim();
            }
        }

        return dict;
    }
}

/// <summary>Pub/Sub inspection tools (PUBSUB CHANNELS / NUMSUB). Read-only: never publishes.</summary>
[McpToolGroup("Pub/Sub")]
public sealed class PubSubTools(IRedisConnectionProvider redis) : RedisToolBase(redis)
{
    [McpTool(
        Name = "pubsub_channels",
        Description = "List the currently active channels matching a pattern (PUBSUB CHANNELS). "
            + "列出当前活跃的发布订阅频道。")]
    public async Task<object?> ChannelsAsync(
        [Description("Channel pattern, e.g. \"orders:*\". 频道匹配模式。")] string pattern = "*",
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var channels = await ctx.Server.ExecuteReadAsync("PUBSUB", "CHANNELS", pattern).ConfigureAwait(false);
            var items = RedisResultHelper.AsArray(channels).Select(r => r.ToString() ?? string.Empty).ToArray();
            return new { pattern, channels = items };
        }, ct);

    [McpTool(
        Name = "pubsub_numsub",
        Description = "Return the number of subscribers for the given channels (PUBSUB NUMSUB). "
            + "返回指定频道的订阅者数量。")]
    public async Task<object?> NumSubAsync(
        [Description("Channel names. 频道名数组。")] string[] channels,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var args = channels.Cast<object>().ToArray();
            var result = await ctx.Server.ExecuteReadAsync("PUBSUB", new object[] { "NUMSUB" }.Concat(args).ToArray()).ConfigureAwait(false);
            var items = RedisResultHelper.AsArray(result);
            var subscriptions = new List<object>();
            for (var i = 0; i + 1 < items.Length; i += 2)
            {
                subscriptions.Add(new
                {
                    channel = items[i].ToString(),
                    subscribers = RedisResultHelper.ToObject(items[i + 1]),
                });
            }

            return new { subscriptions };
        }, ct);
}
