using System.ComponentModel;
using RedisMcpServer.Core.Json;
using RedisMcpServer.Core.Redis;
using RedisMcpServer.Core.Tools;
using StackExchange.Redis;

namespace RedisMcpServer.Tools.Types;

/// <summary>String tools: read values, multiple values, length and ranges. 字符串类型只读查询。</summary>
[McpToolGroup("Strings")]
public sealed class StringTools(IRedisConnectionProvider redis) : RedisToolBase(redis)
{
    [McpTool(
        Name = "string_get",
        Description = "Get the value of a String key. Binary data that is not valid UTF-8 is returned "
            + "as \"base64:...\". 获取字符串键的值。")]
    public async Task<object?> GetAsync(
        [Description(ToolDocs.Key)] string key,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var value = await ctx.Database.StringGetAsync(new RedisKey(key), CommandFlags.None).ConfigureAwait(false);
            return new
            {
                key,
                exists = value.HasValue,
                value = RedisValueHelper.ToDisplayString(value),
            };
        }, ct);

    [McpTool(
        Name = "string_mget",
        Description = "Get the values of multiple String keys in one round-trip (MGET). 一次获取多个字符串键的值。")]
    public async Task<object?> MGetAsync(
        [Description("Array of key names. 键名数组。")] string[] keys,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var redisKeys = keys.Select(k => new RedisKey(k)).ToArray();
            var values = await ctx.Database.StringGetAsync(redisKeys, CommandFlags.None).ConfigureAwait(false);
            return new
            {
                values = keys.Zip(values, (k, v) => new { key = k, exists = v.HasValue, value = RedisValueHelper.ToDisplayString(v) }),
            };
        }, ct);

    [McpTool(
        Name = "string_strlen",
        Description = "Return the length (in bytes) of a String key. 返回字符串键的字节长度。")]
    public async Task<object?> StrLenAsync(
        [Description(ToolDocs.Key)] string key,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var length = await ctx.Database.StringLengthAsync(new RedisKey(key), CommandFlags.None).ConfigureAwait(false);
            return new { key, exists = length > 0, length };
        }, ct);

    [McpTool(
        Name = "string_getrange",
        Description = "Return a substring of a String key by index range (GETRANGE); negative indices count "
            + "from the end. 返回字符串键指定区间的子串。")]
    public async Task<object?> GetRangeAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("Start offset (0-based); negative counts from the end. 起始偏移，负值从末尾计数。")] long start = 0,
        [Description("End offset (inclusive); -1 means the end of the string. 结束偏移（含），-1 表示末尾。")] long end = -1,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var value = await ctx.Database.StringGetRangeAsync(new RedisKey(key), start, end, CommandFlags.None).ConfigureAwait(false);
            return new { key, start, end, value = RedisValueHelper.ToDisplayString(value) };
        }, ct);
}

/// <summary>Hash tools: read individual fields or whole hashes. 哈希类型只读查询。</summary>
[McpToolGroup("Hashes")]
public sealed class HashTools(IRedisConnectionProvider redis) : RedisToolBase(redis)
{
    [McpTool(
        Name = "hash_getall",
        Description = "Return every field and value of a hash key (HGETALL). 返回哈希键的全部字段与值。")]
    public async Task<object?> GetAllAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("Max fields rendered (0 = unlimited). 最多返回的字段数，0 表示不限。")] int limit = 0,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var length = await ctx.Database.HashLengthAsync(new RedisKey(key), CommandFlags.None).ConfigureAwait(false);
            var entries = await ctx.Database.HashGetAllAsync(new RedisKey(key), CommandFlags.None).ConfigureAwait(false);
            var dict = new Dictionary<string, string?>();
            var shown = 0;
            foreach (var entry in entries)
            {
                if (limit > 0 && shown++ >= limit)
                {
                    break;
                }

                dict[RedisValueHelper.ToDisplayString(entry.Name) ?? string.Empty] = RedisValueHelper.ToDisplayString(entry.Value);
            }

            return new
            {
                key,
                length,
                fields = dict,
                truncated = limit > 0 && length > limit,
            };
        }, ct);

    [McpTool(
        Name = "hash_get",
        Description = "Get one field of a hash key (HGET). 获取哈希键中单个字段的值。")]
    public async Task<object?> GetAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("Field name. 字段名。")] string field,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var value = await ctx.Database.HashGetAsync(new RedisKey(key), new RedisValue(field), CommandFlags.None).ConfigureAwait(false);
            return new { key, field, exists = value.HasValue, value = RedisValueHelper.ToDisplayString(value) };
        }, ct);

    [McpTool(
        Name = "hash_mget",
        Description = "Get several fields of a hash key in one round-trip (HMGET). 一次获取哈希键的多个字段。")]
    public async Task<object?> MGetAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("Field names. 字段名数组。")] string[] fields,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var values = await ctx.Database.HashGetAsync(new RedisKey(key), fields.Select(f => new RedisValue(f)).ToArray(), CommandFlags.None).ConfigureAwait(false);
            return new
            {
                key,
                values = fields.Zip(values, (f, v) => new { field = f, exists = v.HasValue, value = RedisValueHelper.ToDisplayString(v) }),
            };
        }, ct);

    [McpTool(
        Name = "hash_keys",
        Description = "List the field names of a hash key (HKEYS). 列出哈希键的所有字段名。")]
    public async Task<object?> KeysAsync(
        [Description(ToolDocs.Key)] string key,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var fields = await ctx.Database.HashKeysAsync(new RedisKey(key), CommandFlags.None).ConfigureAwait(false);
            return new { key, fields = RedisValueHelper.ToDisplayStrings(fields) };
        }, ct);

    [McpTool(
        Name = "hash_values",
        Description = "List the values of a hash key (HVALS). 列出哈希键的所有值。")]
    public async Task<object?> ValuesAsync(
        [Description(ToolDocs.Key)] string key,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var values = await ctx.Database.HashValuesAsync(new RedisKey(key), CommandFlags.None).ConfigureAwait(false);
            return new { key, values = RedisValueHelper.ToDisplayStrings(values) };
        }, ct);

    [McpTool(
        Name = "hash_len",
        Description = "Return the number of fields in a hash key (HLEN). 返回哈希键的字段数量。")]
    public async Task<object?> LenAsync(
        [Description(ToolDocs.Key)] string key,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var length = await ctx.Database.HashLengthAsync(new RedisKey(key), CommandFlags.None).ConfigureAwait(false);
            return new { key, exists = length > 0, length };
        }, ct);

    [McpTool(
        Name = "hash_exists",
        Description = "Check whether a field exists in a hash key (HEXISTS). 检查哈希键中字段是否存在。")]
    public async Task<object?> ExistsAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("Field name. 字段名。")] string field,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var exists = await ctx.Database.HashExistsAsync(new RedisKey(key), new RedisValue(field), CommandFlags.None).ConfigureAwait(false);
            return new { key, field, exists };
        }, ct);
}

/// <summary>List tools: read ranges, length and single elements. 列表类型只读查询。</summary>
[McpToolGroup("Lists")]
public sealed class ListTools(IRedisConnectionProvider redis) : RedisToolBase(redis)
{
    [McpTool(
        Name = "list_range",
        Description = "Return a range of elements of a list key (LRANGE); negative indices count from the "
            + "tail. 返回列表键指定区间的元素。")]
    public async Task<object?> RangeAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("Start index; 0-based, negative counts from the tail. 起始索引。")] long start = 0,
        [Description("Stop index (inclusive); -1 means the end of the list. 结束索引（含）。")] long stop = -1,
        [Description("Include element indexes in the output. 输出是否包含元素索引。")] bool withIndex = false,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var length = await ctx.Database.ListLengthAsync(new RedisKey(key), CommandFlags.None).ConfigureAwait(false);
            var items = await ctx.Database.ListRangeAsync(new RedisKey(key), start, stop, CommandFlags.None).ConfigureAwait(false);
            var rendered = RedisValueHelper.ToDisplayStrings(items);
            return new
            {
                key,
                length,
                start,
                stop,
                items = withIndex ? rendered.Select((value, index) => new { index = start + index, value }) : (object)rendered,
                truncated = length > rendered.Length,
            };
        }, ct);

    [McpTool(
        Name = "list_len",
        Description = "Return the number of elements in a list key (LLEN). 返回列表键的元素数量。")]
    public async Task<object?> LenAsync(
        [Description(ToolDocs.Key)] string key,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var length = await ctx.Database.ListLengthAsync(new RedisKey(key), CommandFlags.None).ConfigureAwait(false);
            return new { key, exists = length > 0, length };
        }, ct);

    [McpTool(
        Name = "list_index",
        Description = "Return the element at a given index of a list key (LINDEX); negative counts from the "
            + "tail. 返回列表键指定索引的元素。")]
    public async Task<object?> IndexAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("Element index. 元素索引。")] long index = 0,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var value = await ctx.Database.ListGetByIndexAsync(new RedisKey(key), index, CommandFlags.None).ConfigureAwait(false);
            return new { key, index, exists = value.HasValue, value = RedisValueHelper.ToDisplayString(value) };
        }, ct);
}

/// <summary>Set tools: read members, cardinality and membership. 集合类型只读查询。</summary>
[McpToolGroup("Sets")]
public sealed class SetTools(IRedisConnectionProvider redis) : RedisToolBase(redis)
{
    [McpTool(
        Name = "set_members",
        Description = "Return all members of a set key (SMEMBERS). 返回集合键的全部成员。")]
    public async Task<object?> MembersAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("Max members rendered (0 = unlimited). 最多显示的成员数，0 表示不限。")] int limit = 0,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var cardinality = await ctx.Database.SetLengthAsync(new RedisKey(key), CommandFlags.None).ConfigureAwait(false);
            var members = await ctx.Database.SetMembersAsync(new RedisKey(key), CommandFlags.None).ConfigureAwait(false);
            var shown = limit > 0 ? members.Take(limit).ToArray() : members;
            return new
            {
                key,
                cardinality,
                members = RedisValueHelper.ToDisplayStrings(shown),
                truncated = limit > 0 && cardinality > limit,
            };
        }, ct);

    [McpTool(
        Name = "set_card",
        Description = "Return the cardinality (number of members) of a set key (SCARD). 返回集合键的基数。")]
    public async Task<object?> CardAsync(
        [Description(ToolDocs.Key)] string key,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var cardinality = await ctx.Database.SetLengthAsync(new RedisKey(key), CommandFlags.None).ConfigureAwait(false);
            return new { key, exists = cardinality > 0, cardinality };
        }, ct);

    [McpTool(
        Name = "set_ismember",
        Description = "Check whether a member belongs to a set key (SISMEMBER). 检查成员是否在集合中。")]
    public async Task<object?> IsMemberAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("Member value. 成员值。")] string member,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var isMember = await ctx.Database.SetContainsAsync(new RedisKey(key), new RedisValue(member), CommandFlags.None).ConfigureAwait(false);
            return new { key, member, isMember };
        }, ct);

    [McpTool(
        Name = "set_randommember",
        Description = "Return one or more random members of a set key (SRANDMEMBER). 随机返回集合的一个或多个成员。")]
    public async Task<object?> RandomMemberAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("How many random members to return (1 by default). 随机返回的成员数量。")] int count = 1,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var members = await ctx.Database.SetRandomMembersAsync(new RedisKey(key), count, CommandFlags.None).ConfigureAwait(false);
            return new { key, count, members = RedisValueHelper.ToDisplayStrings(members) };
        }, ct);
}
