using System.ComponentModel;
using RedisMcpServer.Core.Json;
using RedisMcpServer.Core.Redis;
using RedisMcpServer.Core.Tools;
using StackExchange.Redis;

namespace RedisMcpServer.Tools.Keyspace;

/// <summary>
/// Keyspace tools: database size, non-blocking key enumeration (SCAN), and the
/// "get_key" browser that auto-detects the type and renders the value like
/// RedisDesktopManager's key panel. All read-only.
/// </summary>
[McpToolGroup("Keys")]
public sealed class KeyTools(IRedisConnectionProvider redis) : RedisToolBase(redis)
{
    [McpTool(
        Name = "dbsize",
        Description = "Return the number of keys in the selected database (DBSIZE). 返回当前数据库的键数量。")]
    public async Task<object?> DbSizeAsync(
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var size = (long?)RedisResultHelper.ToObject(await ctx.Database.ExecuteReadAsync("DBSIZE").ConfigureAwait(false)) ?? 0;
            return new { dbsize = size };
        }, ct);

    [McpTool(
        Name = "scan_keys",
        Description = "Scan keys matching a pattern using non-blocking SCAN, with cursor pagination "
            + "and an optional type filter. Preferred over key enumeration on large keyspaces. "
            + "使用 SCAN 游标分页枚举匹配的键，可选项按类型过滤。")]
    public async Task<object?> ScanKeysAsync(
        [Description("SCAN cursor; start at 0 and pass back the returned cursor to page through keys. 游标，从 0 开始。")] long cursor = 0,
        [Description(ToolDocs.Pattern)] string pattern = "*",
        [Description("How many keys to attempt per page (hint only). 每批尝试扫描的键数量。")] long count = 100,
        [Description(ToolDocs.KeyTypeFilter)] string? type = null,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var args = new List<object?> { cursor, "MATCH", pattern, "COUNT", count };
            if (!string.IsNullOrWhiteSpace(type))
            {
                args.Add("TYPE");
                args.Add(type!.Trim());
            }

            var result = await ctx.Database.ExecuteReadAsync("SCAN", args.ToArray()).ConfigureAwait(false);
            var parts = RedisResultHelper.AsArray(result);
            var nextCursor = (long?)RedisResultHelper.ToObject(parts[0]) ?? 0;
            var keys = RedisResultHelper.AsArray(parts[1]).Select(k => k.ToString()).Where(k => k is not null).Cast<string>().ToArray();
            return new { cursor = nextCursor, keys, count, hasMore = nextCursor != 0 };
        }, ct);

    [McpTool(
        Name = "get_key",
        Description = "Inspect one key: auto-detect the Redis type and render its value (strings, hash fields, "
            + "list/set/zset members, stream entries) together with TTL and memory usage — the RDM value-panel "
            + "experience. 查看单个键：自动识别类型并渲染值及 TTL、内存占用。")]
    public async Task<object?> GetKeyAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("Max entries rendered for collection types (hash/list/set/zset/stream). 集合类型最多渲染的条目数。")] int limit = 500,
        [Description("Max characters rendered for string values; longer values are truncated. 字符串值最大显示字符数。")] int maxValueChars = 10_000,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var redisKey = new RedisKey(key);
            var type = await ctx.Database.KeyTypeAsync(redisKey, CommandFlags.None).ConfigureAwait(false);
            if (type == RedisType.None)
            {
                return new { key, exists = false, type = "none" };
            }

            long? ttlSeconds = null, ttlMs = null, memoryUsage = null;
            string? encoding = null;
            try
            {
                ttlSeconds = ToLong(await ctx.Database.ExecuteReadAsync("TTL", redisKey).ConfigureAwait(false));
            }
            catch { /* optional */ }

            try
            {
                ttlMs = ToLong(await ctx.Database.ExecuteReadAsync("PTTL", redisKey).ConfigureAwait(false));
            }
            catch { /* optional */ }

            try
            {
                var usage = await ctx.Database.ExecuteReadAsync("MEMORY", "USAGE", redisKey).ConfigureAwait(false);
                memoryUsage = usage.IsNull ? null : ToLong(usage);
            }
            catch { /* optional */ }

            try
            {
                var enc = await ctx.Database.ExecuteReadAsync("OBJECT", "ENCODING", redisKey).ConfigureAwait(false);
                encoding = enc.IsNull ? null : enc.ToString();
            }
            catch { /* optional */ }

            var reader = KeyValueReaderFactory.Find(type)
                ?? throw new ToolExecutionException($"Unsupported Redis type '{type}' for key '{key}'.");
            var value = await reader.ReadAsync(ctx, redisKey, new ReadLimits(limit, maxValueChars), c).ConfigureAwait(false);

            return new
            {
                key,
                exists = true,
                type = TypeName(type),
                ttlSeconds = ttlSeconds >= 0 ? ttlSeconds : null,
                ttlMs = ttlMs >= 0 ? ttlMs : null,
                memoryUsageBytes = memoryUsage,
                encoding,
                value,
            };
        }, ct);

    [McpTool(
        Name = "key_type",
        Description = "Return the Redis type of a key (string|list|set|zset|hash|stream|none). 返回键的类型。")]
    public async Task<object?> KeyTypeAsync(
        [Description(ToolDocs.Key)] string key,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var type = await ctx.Database.KeyTypeAsync(new RedisKey(key), CommandFlags.None).ConfigureAwait(false);
            return new { key, exists = type != RedisType.None, type = TypeName(type) };
        }, ct);

    [McpTool(
        Name = "key_ttl",
        Description = "Return remaining time-to-live of a key in seconds and milliseconds, plus absolute "
            + "expiration time (EXPIRETIME, Redis ≥ 7). 返回键的剩余生存时间与绝对过期时间。")]
    public async Task<object?> KeyTtlAsync(
        [Description(ToolDocs.Key)] string key,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var redisKey = new RedisKey(key);
            var exists = await ctx.Database.KeyExistsAsync(redisKey, CommandFlags.None).ConfigureAwait(false);
            long? ttlSeconds = null, ttlMs = null, expireTime = null;
            if (exists)
            {
                ttlSeconds = ToLong(await ctx.Database.ExecuteReadAsync("TTL", redisKey).ConfigureAwait(false));
                ttlMs = ToLong(await ctx.Database.ExecuteReadAsync("PTTL", redisKey).ConfigureAwait(false));
                try
                {
                    expireTime = ToLong(await ctx.Database.ExecuteReadAsync("EXPIRETIME", redisKey).ConfigureAwait(false));
                }
                catch
                {
                    // EXPIRETIME is Redis >= 7.0 only
                }
            }

            return new
            {
                key,
                exists,
                ttlSeconds = exists && ttlSeconds >= 0 ? ttlSeconds : null,
                ttlMs = exists && ttlMs >= 0 ? ttlMs : null,
                expireTimeUnix = expireTime,
                noExpiration = exists && ttlSeconds == -1,
            };
        }, ct);

    [McpTool(
        Name = "key_exists",
        Description = "Check whether a key exists and, when present, return its type. 检查键是否存在及类型。")]
    public async Task<object?> KeyExistsAsync(
        [Description(ToolDocs.Key)] string key,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var redisKey = new RedisKey(key);
            var exists = await ctx.Database.KeyExistsAsync(redisKey, CommandFlags.None).ConfigureAwait(false);
            var type = exists ? TypeName(await ctx.Database.KeyTypeAsync(redisKey, CommandFlags.None).ConfigureAwait(false)) : null;
            return new { key, exists, type };
        }, ct);

    [McpTool(
        Name = "key_random",
        Description = "Return a random key from the keyspace (RANDOMKEY). Returns null when the database is empty. "
            + "随机返回一个键，数据库为空时返回 null。")]
    public async Task<object?> KeyRandomAsync(
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var result = await ctx.Database.ExecuteReadAsync("RANDOMKEY").ConfigureAwait(false);
            if (result.IsNull)
            {
                return new { key = (string?)null };
            }

            var key = result.ToString() ?? string.Empty;
            var type = TypeName(await ctx.Database.KeyTypeAsync(new RedisKey(key), CommandFlags.None).ConfigureAwait(false));
            return new { key, type };
        }, ct);

    [McpTool(
        Name = "key_memory",
        Description = "Estimate the memory a key uses (MEMORY USAGE). Returned as null when the command is "
            + "unsupported or the key does not exist. 估算键占用的内存字节数。")]
    public async Task<object?> KeyMemoryAsync(
        [Description(ToolDocs.Key)] string key,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var result = await ctx.Database.ExecuteReadAsync("MEMORY", "USAGE", new RedisKey(key)).ConfigureAwait(false);
            return new { key, memoryBytes = result.IsNull ? (long?)null : ToLong(result) };
        }, ct);

    [McpTool(
        Name = "key_object",
        Description = "Introspect a key's internal representation (OBJECT ENCODING / IDLETIME / FREQ). "
            + "查看键的内部编码、空闲时间与访问频率。")]
    public async Task<object?> KeyObjectAsync(
        [Description(ToolDocs.Key)] string key,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var redisKey = new RedisKey(key);
            string? encoding = null;
            long? idletime = null, freq = null;
            try
            {
                var enc = await ctx.Database.ExecuteReadAsync("OBJECT", "ENCODING", redisKey).ConfigureAwait(false);
                encoding = enc.IsNull ? null : enc.ToString();
            }
            catch { /* optional */ }

            try
            {
                idletime = ToLong(await ctx.Database.ExecuteReadAsync("OBJECT", "IDLETIME", redisKey).ConfigureAwait(false));
            }
            catch { /* optional */ }

            try
            {
                freq = ToLong(await ctx.Database.ExecuteReadAsync("OBJECT", "FREQ", redisKey).ConfigureAwait(false));
            }
            catch { /* optional */ }

            return new { key, encoding, idletimeSeconds = idletime, accessFrequency = freq };
        }, ct);

    internal static string TypeName(RedisType type)
        => type switch
        {
            RedisType.String => "string",
            RedisType.List => "list",
            RedisType.Set => "set",
            RedisType.SortedSet => "zset",
            RedisType.Hash => "hash",
            RedisType.Stream => "stream",
            RedisType.None => "none",
            _ => type.ToString().ToLowerInvariant(),
        };

    private static long? ToLong(RedisResult result)
        => result.IsNull ? null : (long?)RedisResultHelper.ToObject(result);
}
