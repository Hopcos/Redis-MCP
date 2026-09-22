using System.ComponentModel;
using RedisMcpServer.Core.Json;
using RedisMcpServer.Core.Redis;
using RedisMcpServer.Core.Tools;
using StackExchange.Redis;

namespace RedisMcpServer.Tools.Types;

/// <summary>Sorted-set tools: rank/score/lex reads. 有序集合类型只读查询。</summary>
[McpToolGroup("Sorted Sets")]
public sealed class SortedSetTools(IRedisConnectionProvider redis) : RedisToolBase(redis)
{
    [McpTool(
        Name = "zset_range",
        Description = "Return members of a sorted set by rank (index) with their scores (ZRANGE/ZREVRANGE). "
            + "按排名区间返回有序集合成员及其分值。")]
    public async Task<object?> RangeAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("Start rank (0-based). 起始排名。")] long start = 0,
        [Description("Stop rank (inclusive); -1 means the last member. 结束排名（含）。")] long stop = -1,
        [Description("Return members from highest score to lowest. 是否按分值从高到低返回。")] bool reverse = false,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var order = reverse ? Order.Descending : Order.Ascending;
            var cardinality = await FullCardinalityAsync(ctx, key).ConfigureAwait(false);
            var entries = await ctx.Database
                .SortedSetRangeByRankWithScoresAsync(new RedisKey(key), start, stop, order, CommandFlags.None)
                .ConfigureAwait(false);
            return new
            {
                key,
                cardinality,
                reverse,
                items = entries.Select(e => new { member = RedisValueHelper.ToDisplayString(e.Element), score = e.Score }),
            };
        }, ct);

    [McpTool(
        Name = "zset_rangebyscore",
        Description = "Return members whose score falls inside a range (ZRANGEBYSCORE/ZREVRANGEBYSCORE). "
            + "按分值区间返回有序集合成员。")]
    public async Task<object?> RangeByScoreAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("Minimum score; \"-inf\" for unbounded, \"(1.5\" to exclude. 最小分值。")] string min = "-inf",
        [Description("Maximum score; \"+inf\" for unbounded, \"(5\" to exclude. 最大分值。")] string max = "+inf",
        [Description("Return members from highest score to lowest. 是否按分值从高到低返回。")] bool reverse = false,
        [Description("Offset into the result (for pagination). 结果偏移（分页）。")] long offset = 0,
        [Description("How many members to return; negative means all. 返回数量，负数表示全部。")] long count = -1,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var order = reverse ? Order.Descending : Order.Ascending;
            var redisKey = new RedisKey(key);
            var total = await FullCardinalityAsync(ctx, key).ConfigureAwait(false);
            var entries = await ctx.Database
                .SortedSetRangeByScoreWithScoresAsync(redisKey,
                    ParseScoreBound(min, double.NegativeInfinity),
                    ParseScoreBound(max, double.PositiveInfinity),
                    Exclude.None, order, offset, count, CommandFlags.None)
                .ConfigureAwait(false);
            return new
            {
                key,
                min,
                max,
                reverse,
                items = entries.Select(e => new { member = RedisValueHelper.ToDisplayString(e.Element), score = e.Score }),
                total,
            };
        }, ct);

    [McpTool(
        Name = "zset_rangebylex",
        Description = "Return members by lexicographical range (ZRANGEBYLEX), for sorted sets whose members "
            + "share the same score. 按字典序区间返回有序集合成员。")]
    public async Task<object?> RangeByLexAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("Minimum lex bound; \"-\" or \"[a\" inclusive, \"(a\" exclusive. 最小字典序边界。")] string min = "-",
        [Description("Maximum lex bound; \"+\" or \"[z\" inclusive, \"(z\" exclusive. 最大字典序边界。")] string max = "+",
        [Description("Offset into the result (for pagination). 结果偏移（分页）。")] long offset = 0,
        [Description("How many members to return; negative means all. 返回数量，负数表示全部。")] long count = -1,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var redisKey = new RedisKey(key);
            var members = await ctx.Database
                .SortedSetRangeByValueAsync(redisKey, new RedisValue(min), new RedisValue(max), Exclude.None, offset, count, CommandFlags.None)
                .ConfigureAwait(false);
            return new { key, min, max, members = RedisValueHelper.ToDisplayStrings(members) };
        }, ct);

    [McpTool(
        Name = "zset_card",
        Description = "Return the cardinality of a sorted set key (ZCARD). 返回有序集合的元素数量。")]
    public async Task<object?> CardAsync(
        [Description(ToolDocs.Key)] string key,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var cardinality = await FullCardinalityAsync(ctx, key).ConfigureAwait(false);
            return new { key, exists = cardinality > 0, cardinality };
        }, ct);

    [McpTool(
        Name = "zset_score",
        Description = "Return the score of a member in a sorted set (ZSCORE). 返回有序集合中成员的分值。")]
    public async Task<object?> ScoreAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("Member value. 成员值。")] string member,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var score = await ctx.Database.SortedSetScoreAsync(new RedisKey(key), new RedisValue(member), CommandFlags.None).ConfigureAwait(false);
            return new { key, member, exists = score.HasValue, score = score.HasValue ? score.Value : (double?)null };
        }, ct);

    [McpTool(
        Name = "zset_count",
        Description = "Count members whose score falls inside a range (ZCOUNT). 统计分值区间内的成员数量。")]
    public async Task<object?> CountAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("Minimum score; \"-inf\" for unbounded, \"(1.5\" to exclude. 最小分值。")] string min = "-inf",
        [Description("Maximum score; \"+inf\" for unbounded, \"(5\" to exclude. 最大分值。")] string max = "+inf",
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var count = (long?)RedisResultHelper.ToObject(
                await ctx.Database.ExecuteReadAsync("ZCOUNT", new RedisKey(key), min, max).ConfigureAwait(false)) ?? 0;
            return new { key, min, max, count };
        }, ct);

    /// <summary>Full zset cardinality (ZCARD) via the typed API; ±infinity score range covers all members.</summary>
    private static async ValueTask<long> FullCardinalityAsync(RedisToolContext ctx, string key, CancellationToken ct = default)
        => await ctx.Database
            .SortedSetLengthAsync(new RedisKey(key), double.NegativeInfinity, double.PositiveInfinity, Exclude.None, CommandFlags.None)
            .ConfigureAwait(false);

    /// <summary>Parses ZRANGEBYSCORE-style bounds ("-inf", "+inf", "(1.5" → value; exclusion is via the Exclude flags).</summary>
    private static double ParseScoreBound(string value, double fallback)
    {
        var trimmed = value.Trim();
        if (trimmed.Equals("-inf", StringComparison.OrdinalIgnoreCase) || trimmed == "-")
        {
            return double.NegativeInfinity;
        }

        if (trimmed.Equals("+inf", StringComparison.OrdinalIgnoreCase) || trimmed == "+")
        {
            return double.PositiveInfinity;
        }

        if (trimmed.StartsWith('('))
        {
            trimmed = trimmed[1..].Trim();
        }

        return double.TryParse(trimmed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }
}

/// <summary>Stream tools: length, entries, blocks and structure. Stream 类型只读查询。</summary>
[McpToolGroup("Streams")]
public sealed class StreamTools(IRedisConnectionProvider redis) : RedisToolBase(redis)
{
    [McpTool(
        Name = "stream_len",
        Description = "Return the number of entries in a stream key (XLEN). 返回 Stream 中的条目数量。")]
    public async Task<object?> LenAsync(
        [Description(ToolDocs.Key)] string key,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var length = await ctx.Database.StreamLengthAsync(new RedisKey(key), CommandFlags.None).ConfigureAwait(false);
            return new { key, exists = length > 0, length };
        }, ct);

    [McpTool(
        Name = "stream_range",
        Description = "Return entries of a stream between two IDs (XRANGE/XREVRANGE). 按 ID 区间返回 Stream 条目。")]
    public async Task<object?> RangeAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("Start entry ID; \"-\" for the first entry, or a specific ID like \"1-0\". 起始条目 ID。")] string startId = "-",
        [Description("End entry ID; \"+\" for the last entry, or a specific ID. 结束条目 ID。")] string endId = "+",
        [Description("Max entries to return. 最多返回的条目数。")] int count = 100,
        [Description("Return entries from newest to oldest. 是否从新到旧返回。")] bool reverse = false,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var order = reverse ? Order.Descending : Order.Ascending;
            var entries = await ctx.Database
                .StreamRangeAsync(new RedisKey(key), new RedisValue(startId), new RedisValue(endId), count, order, CommandFlags.None)
                .ConfigureAwait(false);
            return new { key, startId, endId, reverse, entries = RenderEntries(entries) };
        }, ct);

    [McpTool(
        Name = "stream_read",
        Description = "Read new entries from a stream after an ID without blocking (XREAD COUNT). "
            + "从指定 ID 之后读取 Stream 新条目（非阻塞）。")]
    public async Task<object?> ReadAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("Read entries with ID greater than this one; \"0\" reads from the start. 起始 ID，\"0\" 从头读取。")] string startId = "0",
        [Description("Max entries to return. 最多返回的条目数。")] int count = 10,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var entries = await ctx.Database
                .StreamReadAsync(new RedisKey(key), new RedisValue(startId), count, CommandFlags.None)
                .ConfigureAwait(false);
            return new { key, startId, entries = RenderEntries(entries) };
        }, ct);

    [McpTool(
        Name = "stream_info",
        Description = "Inspect the structure of a stream (XINFO STREAM): length, last generated ID, groups, "
            + "and the first/last entry. 查看 Stream 的结构信息。")]
    public async Task<object?> InfoAsync(
        [Description(ToolDocs.Key)] string key,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var result = await ctx.Database.ExecuteReadAsync("XINFO", "STREAM", new RedisKey(key)).ConfigureAwait(false);
            return new { key, info = RedisResultHelper.ToObject(result) };
        }, ct);

    private static object RenderEntries(StreamEntry[] entries)
        => entries.Select(e => new
        {
            id = e.Id.ToString(),
            fields = e.Values.ToDictionary(
                v => RedisValueHelper.ToDisplayString(v.Name) ?? string.Empty,
                v => RedisValueHelper.ToDisplayString(v.Value)),
        });
}

/// <summary>Bitmap tools: read bits and bit counts. 位图类型只读查询。</summary>
[McpToolGroup("Bitmaps")]
public sealed class BitmapTools(IRedisConnectionProvider redis) : RedisToolBase(redis)
{
    [McpTool(
        Name = "bitmap_getbit",
        Description = "Return the bit value at an offset of a String key treated as a bitmap (GETBIT). "
            + "获取字符串键按位图解析后指定位的取值。")]
    public async Task<object?> GetBitAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("Bit offset (0-based). 位偏移。")] long offset,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var bit = await ctx.Database.StringGetBitAsync(new RedisKey(key), offset, CommandFlags.None).ConfigureAwait(false);
            return new { key, offset, bit = bit ? 1 : 0 };
        }, ct);

    [McpTool(
        Name = "bitmap_count",
        Description = "Count the bits set to 1 in a byte range of a String key (BITCOUNT). 统计字符串键字节区间内值为 1 的位数。")]
    public async Task<object?> CountAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("Start byte offset (0-based). 起始字节偏移。")] long start = 0,
        [Description("End byte offset; -1 means the end of the string. 结束字节偏移，-1 表示末尾。")] long end = -1,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var count = (long?)(RedisResultHelper.ToObject(
                await ctx.Database.ExecuteReadAsync("BITCOUNT", new RedisKey(key), start, end).ConfigureAwait(false))) ?? 0;
            return new { key, start, end, count };
        }, ct);

    [McpTool(
        Name = "bitmap_pos",
        Description = "Find the first bit set to a given value in a byte range (BITPOS). 查找字节区间内第一个值为指定位的位。")]
    public async Task<object?> PosAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("Target bit value: 0 or 1. 目标位值 0 或 1。")] int bit = 1,
        [Description("Start byte offset. 起始字节偏移。")] long start = 0,
        [Description("End byte offset; -1 means the end. 结束字节偏移，-1 表示末尾。")] long end = -1,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var position = (long?)(RedisResultHelper.ToObject(
                await ctx.Database.ExecuteReadAsync("BITPOS", new RedisKey(key), bit, start, end).ConfigureAwait(false)));
            return new { key, bit, start, end, position = position < 0 ? null : position };
        }, ct);
}

/// <summary>HyperLogLog tools: approximate cardinality (PFCOUNT). HyperLogLog 基数统计。</summary>
[McpToolGroup("HyperLogLog")]
public sealed class HyperLogLogTools(IRedisConnectionProvider redis) : RedisToolBase(redis)
{
    [McpTool(
        Name = "hll_count",
        Description = "Return the approximate cardinality of a HyperLogLog key (PFCOUNT). "
            + "返回 HyperLogLog 键的近似基数。")]
    public async Task<object?> CountAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("Optional second key; both are merged for the count. 可选的第二个键，会合并计数。")] string? otherKey = null,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var keys = otherKey is null
                ? new[] { new RedisKey(key) }
                : new[] { new RedisKey(key), new RedisKey(otherKey) };
            var count = await ctx.Database.HyperLogLogLengthAsync(keys, CommandFlags.None).ConfigureAwait(false);
            return new { key, otherKey, count };
        }, ct);
}

/// <summary>GEO tools: positions, distances, hashes and radius searches. GEO 空间索引只读查询。</summary>
[McpToolGroup("Geo")]
public sealed class GeoTools(IRedisConnectionProvider redis) : RedisToolBase(redis)
{
    [McpTool(
        Name = "geo_pos",
        Description = "Return the longitude/latitude of members of a geo key (GEOPOS). 返回地理键中成员的经纬度。")]
    public async Task<object?> PosAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("Member name. 成员名。")] string member,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var position = await ctx.Database.GeoPositionAsync(new RedisKey(key), new RedisValue(member), CommandFlags.None).ConfigureAwait(false);
            return new
            {
                key,
                member,
                exists = position.HasValue,
                longitude = position.HasValue ? position.Value.Longitude : (double?)null,
                latitude = position.HasValue ? position.Value.Latitude : (double?)null,
            };
        }, ct);

    [McpTool(
        Name = "geo_dist",
        Description = "Return the distance between two members of a geo key (GEODIST). 返回地理键中两个成员间的距离。")]
    public async Task<object?> DistAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("First member. 第一个成员。")] string member1,
        [Description("Second member. 第二个成员。")] string member2,
        [Description("Distance unit. 距离单位。")]
        [ToolParamEnum("m", "km", "mi", "ft")]
        string unit = "km",
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var distance = await ctx.Database
                .GeoDistanceAsync(new RedisKey(key), new RedisValue(member1), new RedisValue(member2), ParseUnit(unit), CommandFlags.None)
                .ConfigureAwait(false);
            return new { key, member1, member2, unit, exists = distance.HasValue, distance = distance.HasValue ? distance.Value : (double?)null };
        }, ct);

    [McpTool(
        Name = "geo_hash",
        Description = "Return the standard geohash string of members of a geo key (GEOHASH). 返回地理键中成员的标准 geohash。")]
    public async Task<object?> HashAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("Member name. 成员名。")] string member,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var hash = await ctx.Database.GeoHashAsync(new RedisKey(key), new RedisValue(member), CommandFlags.None).ConfigureAwait(false);
            return new { key, member, geohash = hash };
        }, ct);

    [McpTool(
        Name = "geo_search",
        Description = "Search members of a geo key within a radius around a center (GEOSEARCH/GEORADIUS). "
            + "以某点为中心按半径搜索地理键中的成员。")]
    public async Task<object?> SearchAsync(
        [Description(ToolDocs.Key)] string key,
        [Description("Center longitude; use together with latitude. 中心经度。")] double longitude = double.NaN,
        [Description("Center latitude; use together with longitude. 中心纬度。")] double latitude = double.NaN,
        [Description("Alternative center: a member name already stored in the key. 备选中心：键中已存成员名。")] string? centerMember = null,
        [Description("Search radius. 搜索半径。")] double radius = 1.0,
        [Description("Radius unit. 半径单位。")]
        [ToolParamEnum("m", "km", "mi", "ft")]
        string unit = "km",
        [Description("Max results returned. 最多返回结果数。")] int count = 100,
        [Description("Sort results by distance ascending. 按距离升序排序。")] bool sortAsc = true,
        [Description(ToolDocs.Environment)] string? environment = null,
        CancellationToken ct = default)
        => await RunAsync(environment, async (ctx, c) =>
        {
            var geoUnit = ParseUnit(unit);
            var order = sortAsc ? Order.Ascending : Order.Descending;
            var options = GeoRadiusOptions.WithCoordinates | GeoRadiusOptions.WithDistance;
            GeoRadiusResult[] results;
            if (!string.IsNullOrWhiteSpace(centerMember))
            {
                results = await ctx.Database
                    .GeoRadiusAsync(new RedisKey(key), new RedisValue(centerMember), radius, geoUnit, count, order, options, CommandFlags.None)
                    .ConfigureAwait(false);
            }
            else
            {
                results = await ctx.Database
                    .GeoRadiusAsync(new RedisKey(key), longitude, latitude, radius, geoUnit, count, order, options, CommandFlags.None)
                    .ConfigureAwait(false);
            }

            return new
            {
                key,
                center = string.IsNullOrWhiteSpace(centerMember)
                    ? new { longitude, latitude }
                    : (object)new { member = centerMember },
                radius,
                unit,
                results = results.Select(r => new
                {
                    member = RedisValueHelper.ToDisplayString(r.Member),
                    distance = r.Distance,
                    longitude = r.Position?.Longitude,
                    latitude = r.Position?.Latitude,
                }),
            };
        }, ct);

    private static GeoUnit ParseUnit(string unit)
        => unit.Trim().ToLowerInvariant() switch
        {
            "m" => GeoUnit.Meters,
            "mi" => GeoUnit.Miles,
            "ft" => GeoUnit.Feet,
            _ => GeoUnit.Kilometers,
        };
}
