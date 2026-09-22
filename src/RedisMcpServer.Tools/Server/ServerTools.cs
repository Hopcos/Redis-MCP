using System.ComponentModel;
using System.Globalization;
using RedisMcpServer.Core.Json;
using RedisMcpServer.Core.Redis;
using RedisMcpServer.Core.Tools;
using StackExchange.Redis;

namespace RedisMcpServer.Tools.Server;

/// <summary>
/// Server-inspection tools (INFO, CONFIG GET, CLIENT LIST, TIME, SLOWLOG, LATENCY, MEMORY).
/// All target the first endpoint of the selected environment, like RedisDesktopManager's server panel.
/// </summary>
[McpToolGroup("Server")]
public sealed class ServerTools(IRedisConnectionProvider redis) : RedisToolBase(redis)
{
    [McpTool(
        Name = "server_info",
        Description = "Return Redis server INFO. Sections can be filtered, e.g. \"server\", \"clients\", "
            + "\"memory\", \"stats\", \"replication\", \"keyspace\". 返回 Redis 服务器信息，可按 Section 过滤。")]
    public async Task<object?> InfoAsync(
        [Description("Optional INFO section name (all sections when omitted). 可选的 INFO 分区名，省略时返回全部。")] string? section = null,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var raw = await ctx.Server.ExecuteReadAsync("INFO", section ?? "all").ConfigureAwait(false);
            return new { sections = ParseInfoSections(raw.ToString() ?? string.Empty) };
        }, ct);

    [McpTool(
        Name = "server_config",
        Description = "Query the server configuration (CONFIG GET). 查询服务器运行配置。")]
    public async Task<object?> ConfigAsync(
        [Description("Parameter name pattern, e.g. \"maxmemory*\", \"save\", \"*timeout*\". 配置项匹配模式。")] string pattern = "*",
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var result = await ctx.Server.ExecuteReadAsync("CONFIG", "GET", pattern).ConfigureAwait(false);
            return new { pattern, config = RedisResultHelper.ToFlatDictionary(result) };
        }, ct);

    [McpTool(
        Name = "server_client_list",
        Description = "List the clients currently connected to the server (CLIENT LIST). 列出当前连接的客户端。")]
    public async Task<object?> ClientListAsync(
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var raw = await ctx.Server.ExecuteReadAsync("CLIENT", "LIST").ConfigureAwait(false);
            var lines = (raw.ToString() ?? string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new { clients = lines.Select(ParseClientLine) };
        }, ct);

    [McpTool(
        Name = "server_time",
        Description = "Return the server clock (TIME) as UNIX seconds/microseconds and ISO-8601 UTC. "
            + "返回服务器时间。")]
    public async Task<object?> TimeAsync(
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var result = await ctx.Server.TimeAsync().ConfigureAwait(false);
            var unix = new DateTimeOffset(result).ToUnixTimeSeconds();
            return new
            {
                unixSeconds = unix,
                isoUtc = result.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            };
        }, ct);

    [McpTool(
        Name = "server_lastsave",
        Description = "Return the timestamp of the last successful SAVE (LASTSAVE). 返回最近一次成功保存的时间。")]
    public async Task<object?> LastSaveAsync(
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var lastSave = await ctx.Server.LastSaveAsync().ConfigureAwait(false);
            var unix = new DateTimeOffset(lastSave).ToUnixTimeSeconds();
            var ago = DateTimeOffset.UtcNow - lastSave;
            return new
            {
                unixSeconds = unix,
                isoUtc = lastSave.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                agoSeconds = (long)ago.TotalSeconds,
            };
        }, ct);

    [McpTool(
        Name = "server_slowlog",
        Description = "Return recent slow commands recorded by SLOWLOG. 返回最近的慢查询日志。")]
    public async Task<object?> SlowlogAsync(
        [Description("How many entries to return (default 100). 返回条数。")] int count = 100,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var result = await ctx.Server.ExecuteReadAsync(
                "SLOWLOG", "GET", count.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
            var rows = RedisResultHelper.AsArray(result);
            var entries = rows.Select(raw =>
            {
                var parts = RedisResultHelper.AsArray(raw).Select(RedisResultHelper.ToObject).ToArray();
                return new
                {
                    id = parts.ElementAtOrDefault(0),
                    timestampUnix = parts.ElementAtOrDefault(1),
                    durationMicros = parts.ElementAtOrDefault(2),
                    arguments = parts.ElementAtOrDefault(3),
                };
            });
            return new { entries };
        }, ct);

    [McpTool(
        Name = "server_latency",
        Description = "Return the latest latency monitoring events (LATENCY LATEST). 返回延迟监控的最新事件。")]
    public async Task<object?> LatencyAsync(
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var result = await ctx.Server.ExecuteReadAsync("LATENCY", "LATEST").ConfigureAwait(false);
            var rows = RedisResultHelper.AsArray(result);
            var events = rows.Select(raw =>
            {
                var parts = RedisResultHelper.AsArray(raw).Select(RedisResultHelper.ToObject).ToArray();
                return new
                {
                    eventName = parts.ElementAtOrDefault(0),
                    timestampUnix = parts.ElementAtOrDefault(1),
                    latestMs = parts.ElementAtOrDefault(2),
                    maxMs = parts.ElementAtOrDefault(3),
                };
            });
            return new { events };
        }, ct);

    [McpTool(
        Name = "server_memory",
        Description = "Return memory statistics of the server (MEMORY STATS). 返回服务器内存统计。")]
    public async Task<object?> MemoryAsync(
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var result = await ctx.Server.ExecuteReadAsync("MEMORY", "STATS").ConfigureAwait(false);
            return new { stats = ToDictionary(result) };
        }, ct);

    private static object ToDictionary(RedisResult result)
    {
        if (result.IsNull || result.Length < 0)
        {
            return RedisResultHelper.ToObject(result) ?? new object();
        }

        var dict = new Dictionary<object, object>();
        var items = RedisResultHelper.AsArray(result);
        for (var i = 0; i + 1 < items.Length; i += 2)
        {
            var key = RedisResultHelper.ToObject(items[i]) ?? string.Empty;
            var value = RedisResultHelper.ToObject(items[i + 1]);
            dict[key] = value ?? string.Empty;
        }

        return dict;
    }

    private static object ParseClientLine(string line)
    {
        var dict = new Dictionary<string, string>();
        foreach (var token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = token.IndexOf('=');
            if (idx > 0)
            {
                dict[token[..idx]] = token[(idx + 1)..];
            }
        }

        return dict;
    }

    private static Dictionary<string, Dictionary<string, string>> ParseInfoSections(string raw)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? current = null;
        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.StartsWith('#'))
            {
                var name = trimmed[1..].Trim();
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                sections[name] = current;
                continue;
            }

            current ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var idx = trimmed.IndexOf(':');
            if (idx > 0)
            {
                current[trimmed[..idx].Trim()] = trimmed[(idx + 1)..].Trim();
            }
        }

        return sections;
    }
}
