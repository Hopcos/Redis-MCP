using RedisMcpServer.Core.Json;
using RedisMcpServer.Core.Redis;
using StackExchange.Redis;

namespace RedisMcpServer.Tools.Keyspace;

/// <summary>Read limits applied when rendering key values (mimics RedisDesktopManager's value panel).</summary>
public sealed record ReadLimits(int MaxEntries, int MaxValueChars);

/// <summary>
/// Strategy that renders the value of one Redis data type (the RDM "view value" experience).
/// Implementations are selected by <see cref="KeyValueReaderFactory"/> based on KEY TYPE.
/// </summary>
public interface IKeyValueReader
{
    RedisType Kind { get; }

    ValueTask<object?> ReadAsync(RedisToolContext ctx, RedisKey key, ReadLimits limits, CancellationToken ct);
}

/// <summary>Selects the matching strategy for a Redis type.</summary>
public static class KeyValueReaderFactory
{
    public static readonly IReadOnlyList<IKeyValueReader> All =
    [
        new StringValueReader(),
        new HashValueReader(),
        new ListValueReader(),
        new SetValueReader(),
        new SortedSetValueReader(),
        new StreamValueReader(),
    ];

    public static IKeyValueReader? Find(RedisType type)
        => All.FirstOrDefault(reader => reader.Kind == type);
}

/// <summary>String value reader (also covers bitmaps / HyperLogLog stored as strings at a byte level).</summary>
public sealed class StringValueReader : IKeyValueReader
{
    public RedisType Kind => RedisType.String;

    public async ValueTask<object?> ReadAsync(RedisToolContext ctx, RedisKey key, ReadLimits limits, CancellationToken ct)
    {
        var value = await ctx.Database.StringGetAsync(key, CommandFlags.None).ConfigureAwait(false);
        var length = await ctx.Database.StringLengthAsync(key, CommandFlags.None).ConfigureAwait(false);
        var text = RedisValueHelper.ToDisplayString(value) ?? string.Empty;
        var truncated = text.Length > limits.MaxValueChars;
        if (truncated)
        {
            text = text[..limits.MaxValueChars] + $"\n… (truncated to {limits.MaxValueChars} chars, total length {length})";
        }

        return new
        {
            type = "string",
            length = length,
            value = text,
            truncated,
        };
    }
}

/// <summary>Hash reader (fields → values, capped).</summary>
public sealed class HashValueReader : IKeyValueReader
{
    public RedisType Kind => RedisType.Hash;

    public async ValueTask<object?> ReadAsync(RedisToolContext ctx, RedisKey key, ReadLimits limits, CancellationToken ct)
    {
        var length = await ctx.Database.HashLengthAsync(key, CommandFlags.None).ConfigureAwait(false);
        var entries = await ctx.Database.HashGetAllAsync(key, CommandFlags.None).ConfigureAwait(false);
        var fields = new Dictionary<string, string?>();
        var shown = 0;
        foreach (var entry in entries)
        {
            if (shown++ >= limits.MaxEntries)
            {
                break;
            }

            fields[RedisValueHelper.ToDisplayString(entry.Name) ?? string.Empty] =
                RedisValueHelper.ToDisplayString(entry.Value);
        }

        return new
        {
            type = "hash",
            length = length,
            fields,
            truncated = entries.Length > limits.MaxEntries,
        };
    }
}

/// <summary>List reader (items, capped).</summary>
public sealed class ListValueReader : IKeyValueReader
{
    public RedisType Kind => RedisType.List;

    public async ValueTask<object?> ReadAsync(RedisToolContext ctx, RedisKey key, ReadLimits limits, CancellationToken ct)
    {
        var length = await ctx.Database.ListLengthAsync(key, CommandFlags.None).ConfigureAwait(false);
        var items = await ctx.Database.ListRangeAsync(key, 0, limits.MaxEntries - 1, CommandFlags.None).ConfigureAwait(false);
        return new
        {
            type = "list",
            length = length,
            items = RedisValueHelper.ToDisplayStrings(items),
            truncated = length > limits.MaxEntries,
        };
    }
}

/// <summary>Set reader (members, capped for display).</summary>
public sealed class SetValueReader : IKeyValueReader
{
    public RedisType Kind => RedisType.Set;

    public async ValueTask<object?> ReadAsync(RedisToolContext ctx, RedisKey key, ReadLimits limits, CancellationToken ct)
    {
        var cardinality = await ctx.Database.SetLengthAsync(key, CommandFlags.None).ConfigureAwait(false);
        var members = await ctx.Database.SetMembersAsync(key, CommandFlags.None).ConfigureAwait(false);
        var shown = members.Take(limits.MaxEntries).ToArray();
        return new
        {
            type = "set",
            cardinality = cardinality,
            members = RedisValueHelper.ToDisplayStrings(shown),
            truncated = members.Length > limits.MaxEntries,
        };
    }
}

/// <summary>Sorted set reader (member + score, capped).</summary>
public sealed class SortedSetValueReader : IKeyValueReader
{
    public RedisType Kind => RedisType.SortedSet;

    public async ValueTask<object?> ReadAsync(RedisToolContext ctx, RedisKey key, ReadLimits limits, CancellationToken ct)
    {
        var cardinality = await ctx.Database
            .SortedSetLengthAsync(key, double.NegativeInfinity, double.PositiveInfinity, Exclude.None, CommandFlags.None)
            .ConfigureAwait(false);
        var entries = await ctx.Database
            .SortedSetRangeByRankWithScoresAsync(key, 0, limits.MaxEntries - 1, Order.Ascending, CommandFlags.None)
            .ConfigureAwait(false);
        var items = entries.Select(e => new
        {
            member = RedisValueHelper.ToDisplayString(e.Element),
            score = e.Score,
        });
        return new
        {
            type = "zset",
            cardinality = cardinality,
            items,
            truncated = cardinality > limits.MaxEntries,
        };
    }
}

/// <summary>Stream reader (recent entries, capped).</summary>
public sealed class StreamValueReader : IKeyValueReader
{
    public RedisType Kind => RedisType.Stream;

    public async ValueTask<object?> ReadAsync(RedisToolContext ctx, RedisKey key, ReadLimits limits, CancellationToken ct)
    {
        var length = await ctx.Database.StreamLengthAsync(key, CommandFlags.None).ConfigureAwait(false);
        var entries = await ctx.Database.StreamRangeAsync(key, "-", "+", limits.MaxEntries, Order.Ascending, CommandFlags.None)
            .ConfigureAwait(false);
        var items = entries.Select(e => new
        {
            id = e.Id.ToString(),
            fields = e.Values.ToDictionary(
                v => RedisValueHelper.ToDisplayString(v.Name) ?? string.Empty,
                v => RedisValueHelper.ToDisplayString(v.Value)),
        });
        return new
        {
            type = "stream",
            length = length,
            items,
            truncated = length > limits.MaxEntries,
        };
    }
}
