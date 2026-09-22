using System.Runtime.CompilerServices;
using System.Text;
using RedisMcpServer.Core.Configuration;
using RedisMcpServer.Core.Environment;
using RedisMcpServer.Core.Json;
using RedisMcpServer.Core.Redis;
using RedisMcpServer.Core.Tools;
using StackExchange.Redis;

namespace RedisMcpServer.Core.Browse;

/// <summary>
/// Implementation of <see cref="IRedisBrowseService"/> on top of <see cref="IRedisConnectionProvider"/>.
/// A single multiplexer per environment serves every logical database via <c>GetDatabase(db)</c>.
/// </summary>
public sealed class RedisBrowseService : IRedisBrowseService
{
    private const int ScanPageSize = 1_000;
    private const int ValueElementCap = 20;
    private const int ValueElementLengthCap = 200;
    private const int StringValueLengthCap = 2_000;

    private readonly IRedisConnectionProvider _provider;
    private readonly IRedisEnvironmentResolver _resolver;

    public RedisBrowseService(IRedisConnectionProvider provider, IRedisEnvironmentResolver resolver)
    {
        _provider = provider;
        _resolver = resolver;
    }

    public async Task<IReadOnlyList<DatabaseSummary>> ListDatabasesAsync(string? environment, CancellationToken cancellationToken)
    {
        var multiplexer = await GetMultiplexerCheckedAsync(environment, cancellationToken).ConfigureAwait(false);

        int databases = await ReadDatabaseCountAsync(multiplexer, cancellationToken).ConfigureAwait(false);
        var expiresByDb = await ReadExpiresByDbAsync(multiplexer, cancellationToken).ConfigureAwait(false);

        var summaries = new List<DatabaseSummary>(databases);
        for (var db = 0; db < databases; db++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var database = multiplexer.GetDatabase(db);
            var keys = (long)(await database.ExecuteReadAsync("DBSIZE").ConfigureAwait(false))!;
            summaries.Add(new DatabaseSummary(db, keys, expiresByDb.GetValueOrDefault(db, 0)));
        }

        return summaries;
    }

    public async Task<TypeSummary> CountTypesAsync(string? environment, int? db, CancellationToken cancellationToken)
    {
        var (multiplexer, _, dbIndex) = await ResolveDatabaseAsync(environment, db, cancellationToken).ConfigureAwait(false);
        var database = multiplexer.GetDatabase(dbIndex);

        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        await foreach (var key in ScanAllAsync(database, typeFilter: null, cancellationToken).ConfigureAwait(false))
        {
            var type = (await database.KeyTypeAsync(key).ConfigureAwait(false)).ToString().ToLowerInvariant();
            counts[type] = counts.GetValueOrDefault(type) + 1;
            total++;
        }

        var types = counts
            .OrderByDescending(p => p.Value)
            .Select(p => new TypeCount(p.Key, p.Value))
            .ToArray();
        return new TypeSummary(dbIndex, total, types);
    }

    public async Task<QueryResult> QueryAsync(KeyQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();

        var (multiplexer, environmentInfo, dbIndex) = await ResolveDatabaseAsync(query.Environment, query.Db, cancellationToken).ConfigureAwait(false);
        var database = multiplexer.GetDatabase(dbIndex);
        var now = DateTimeOffset.UtcNow;

        var typeFilter = string.IsNullOrWhiteSpace(query.Type) ? null : query.Type.Trim();
        var keyPattern = string.IsNullOrWhiteSpace(query.KeyPattern) ? null : query.KeyPattern;
        var valuePattern = string.IsNullOrWhiteSpace(query.ValuePattern) ? null : query.ValuePattern;

        var rows = new List<KeyRow>(ScanPageSize * 4);
        await foreach (var key in ScanAllAsync(database, typeFilter, cancellationToken).ConfigureAwait(false))
        {
            if (keyPattern is not null && !key.ToString().Contains(keyPattern, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var type = (await database.KeyTypeAsync(key).ConfigureAwait(false)).ToString().ToLowerInvariant();
            if (typeFilter is not null && !string.Equals(type, typeFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = await RenderValueAsync(database, key, type, cancellationToken).ConfigureAwait(false);
            if (valuePattern is not null && (value is null || !value.Contains(valuePattern, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // Redis does not store a creation timestamp. Estimate age from OBJECT IDLETIME where the
            // server supports it (Redis 7+ / LRU policies); on Redis 6.0 with an LFU policy the command
            // is unavailable or minute-coarse (frequently 0 for hot keys), so createdAt stays null and
            // the raw idle/frequency data is reported instead.
            long? idleSeconds = null;
            DateTimeOffset? lastAccessAt = null;
            DateTimeOffset? createdAt = null;
            try
            {
                var idleResult = await database.ExecuteReadAsync("OBJECT", "IDLETIME", key).ConfigureAwait(false);
                if (!idleResult.IsNull && long.TryParse(idleResult.ToString(), out var idle) && idle >= 0)
                {
                    idleSeconds = idle;
                    lastAccessAt = now.AddSeconds(-idle);
                    if (idle > 0)
                    {
                        createdAt = lastAccessAt;
                    }
                }
            }
            catch (RedisException)
            {
                // OBJECT unavailable on this server; creation/access times stay null.
            }

            long? accessFrequency = null;
            try
            {
                var freqResult = await database.ExecuteReadAsync("OBJECT", "FREQ", key).ConfigureAwait(false);
                if (!freqResult.IsNull && long.TryParse(freqResult.ToString(), out var freq) && freq >= 0)
                {
                    accessFrequency = freq;
                }
            }
            catch (RedisException)
            {
                // OBJECT FREQ requires an LFU maxmemory policy; otherwise null.
            }

            long? ttlSeconds = null;
            var ttl = await database.KeyTimeToLiveAsync(key).ConfigureAwait(false);
            if (ttl is not null && ttl.Value.TotalSeconds >= 1)
            {
                ttlSeconds = (long)ttl.Value.TotalSeconds;
            }

            rows.Add(new KeyRow(createdAt, type, key.ToString() ?? string.Empty, value, ttlSeconds, idleSeconds, lastAccessAt, accessFrequency));
        }

        var total = rows.Count;
        var pageSize = query.PageSize;
        var pageNumber = query.PageNumber;
        var totalPages = total == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        if (pageNumber > totalPages && totalPages > 0)
        {
            pageNumber = totalPages; // clamp instead of returning an empty tail page
        }

        // Rows without a time estimate sort last in both directions.
        var ordered = query.IsDescendingOrder
            ? rows.OrderBy(r => r.CreatedAt.HasValue ? 1 : 0).ThenByDescending(r => r.CreatedAt)
            : rows.OrderBy(r => r.CreatedAt.HasValue ? 1 : 0).ThenBy(r => r.CreatedAt);

        var items = ordered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToArray();
        return new QueryResult(pageNumber, pageSize, total, totalPages, environmentInfo.Name, dbIndex, items);
    }

    private async Task<ConnectionMultiplexer> GetMultiplexerCheckedAsync(string? environment, CancellationToken cancellationToken)
    {
        var environmentInfo = _resolver.Resolve(environment);
        try
        {
            return await _provider.GetMultiplexerAsync(environmentInfo.Name, cancellationToken).ConfigureAwait(false);
        }
        catch (RedisException ex)
        {
            throw new ToolExecutionException($"Redis error on environment '{environmentInfo.Name}': {ex.Message}", ex);
        }
        catch (TimeoutException ex)
        {
            throw new ToolExecutionException($"Redis timeout on environment '{environmentInfo.Name}': {ex.Message}", ex);
        }
        catch (IOException ex)
        {
            throw new ToolExecutionException($"Redis I/O error on environment '{environmentInfo.Name}': {ex.Message}", ex);
        }
    }

    private async Task<(ConnectionMultiplexer Multiplexer, RedisEnvironment Environment, int DbIndex)> ResolveDatabaseAsync(
        string? environment, int? db, CancellationToken cancellationToken)
    {
        var multiplexer = await GetMultiplexerCheckedAsync(environment, cancellationToken).ConfigureAwait(false);
        var environmentInfo = _resolver.Resolve(environment);
        var dbIndex = db ?? environmentInfo.DefaultDatabase;

        if (dbIndex < 0)
        {
            throw new ToolExecutionException($"Parameter 'db' must be >= 0, got {dbIndex}.");
        }

        var databases = await ReadDatabaseCountAsync(multiplexer, cancellationToken).ConfigureAwait(false);
        if (dbIndex >= databases)
        {
            throw new ToolExecutionException(
                $"Database {dbIndex} is out of range. Environment '{environmentInfo.Name}' has {databases} databases (0..{databases - 1}).");
        }

        return (multiplexer, environmentInfo, dbIndex);
    }

    private static async Task<int> ReadDatabaseCountAsync(ConnectionMultiplexer multiplexer, CancellationToken cancellationToken)
    {
        var server = multiplexer.GetServer(multiplexer.GetEndPoints(configuredOnly: true)[0]);
        var result = await server.ExecuteReadAsync("CONFIG", "GET", "databases").ConfigureAwait(false);
        if (!result.IsNull && result.Length >= 2 && int.TryParse(result[1].ToString(), out var count) && count > 0)
        {
            return count;
        }

        return 16;
    }

    private static async Task<Dictionary<int, long>> ReadExpiresByDbAsync(ConnectionMultiplexer multiplexer, CancellationToken cancellationToken)
    {
        var server = multiplexer.GetServer(multiplexer.GetEndPoints(configuredOnly: true)[0]);
        var info = await server.InfoAsync("keyspace").ConfigureAwait(false);
        var expires = new Dictionary<int, long>();
        foreach (var section in info)
        {
            foreach (var pair in section)
            {
                if (pair.Key is null || !pair.Key.StartsWith("db", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = pair.Key.ToString();
                var colon = name.IndexOf(':');
                if (colon < 0 || !int.TryParse(name.AsSpan(2, colon - 2), out var dbIndex))
                {
                    continue;
                }

                var fields = pair.Value.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var field in fields)
                {
                    var eq = field.IndexOf('=');
                    if (eq > 0 && field.AsSpan(0, eq).Equals("expires", StringComparison.OrdinalIgnoreCase)
                        && long.TryParse(field.AsSpan(eq + 1), out var exp))
                    {
                        expires[dbIndex] = exp;
                    }
                }
            }
        }

        return expires;
    }

    /// <summary>Full non-blocking SCAN over a database, optionally restricted to one TYPE (best-effort per Redis SCAN semantics).</summary>
    private static async IAsyncEnumerable<RedisKey> ScanAllAsync(
        IDatabase database, string? typeFilter, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long cursor = 0;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var args = new List<object> { cursor, "MATCH", "*", "COUNT", ScanPageSize };
            if (typeFilter is not null)
            {
                args.Add("TYPE");
                args.Add(typeFilter);
            }

            var result = await database.ExecuteAsync("SCAN", args, CommandFlags.None).ConfigureAwait(false);
            var values = RedisResultHelper.AsArray(result);
            cursor = long.Parse(values[0].ToString() ?? "0");
            var keys = values[1];
            if (keys.Length >= 0)
            {
                for (var i = 0; i < keys.Length; i++)
                {
                    yield return (RedisKey)(keys[i].ToString() ?? string.Empty);
                }
            }
        }
        while (cursor != 0);
    }

    /// <summary>Renders a key's value as bounded JSON text (string raw; collections capped), or null when the value is empty.</summary>
    private static async Task<string?> RenderValueAsync(IDatabase database, RedisKey key, string type, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            switch (type)
            {
                case "string":
                {
                    var value = await database.StringGetAsync(key).ConfigureAwait(false);
                    return value.IsNullOrEmpty ? null : Truncate(value.ToString(), StringValueLengthCap);
                }

                case "hash":
                {
                    var entries = await database.HashGetAllAsync(key).ConfigureAwait(false);
                    if (entries.Length == 0)
                    {
                        return null;
                    }

                    var builder = new StringBuilder("{");
                    var shown = 0;
                    for (; shown < Math.Min(entries.Length, ValueElementCap); shown++)
                    {
                        if (shown > 0)
                        {
                            builder.Append(", ");
                        }

                        builder.Append('"').Append(Truncate(entries[shown].Name.ToString(), ValueElementLengthCap)).Append("\": \"")
                               .Append(Truncate(entries[shown].Value.ToString(), ValueElementLengthCap)).Append('"');
                    }

                    if (entries.Length > ValueElementCap)
                    {
                        builder.Append(", ...");
                    }

                    return builder.Append('}').ToString();
                }

                case "list":
                {
                    var values = await database.ListRangeAsync(key, 0, ValueElementCap - 1).ConfigureAwait(false);
                    return RenderArray(values.Select(v => Truncate(v.ToString(), ValueElementLengthCap)).ToArray(), values.Length);
                }

                case "set":
                {
                    var values = await database.SetMembersAsync(key).ConfigureAwait(false);
                    var taken = values.Take(ValueElementCap).Select(v => Truncate(v.ToString(), ValueElementLengthCap)).ToArray();
                    return RenderArray(taken, values.Length);
                }

                case "zset":
                {
                    var entries = await database.SortedSetRangeByRankWithScoresAsync(key, 0, ValueElementCap - 1).ConfigureAwait(false);
                    if (entries.Length == 0)
                    {
                        return null;
                    }

                    var builder = new StringBuilder("[");
                    for (var i = 0; i < entries.Length; i++)
                    {
                        if (i > 0)
                        {
                            builder.Append(", ");
                        }

                        builder.Append('"').Append(Truncate(entries[i].Element.ToString(), ValueElementLengthCap))
                               .Append("\": ").Append(entries[i].Score.ToString("0.####"));
                    }

                    return builder.Append(']').ToString();
                }

                case "stream":
                {
                    var entries = await database.StreamRangeAsync(key, "-", "+", ValueElementCap, Order.Ascending).ConfigureAwait(false);
                    if (entries.Length == 0)
                    {
                        return null;
                    }

                    var builder = new StringBuilder("[");
                    for (var i = 0; i < entries.Length; i++)
                    {
                        if (i > 0)
                        {
                            builder.Append(", ");
                        }

                        builder.Append('"').Append(entries[i].Id).Append("\": {");
                        var fields = entries[i].Values;
                        for (var f = 0; f < fields.Length; f++)
                        {
                            if (f > 0)
                            {
                                builder.Append(", ");
                            }

                            builder.Append('"').Append(fields[f].Name).Append("\": \"")
                                   .Append(Truncate(fields[f].Value.ToString(), ValueElementLengthCap)).Append('"');
                        }

                        builder.Append('}');
                    }

                    return builder.Append(']').ToString();
                }

                default:
                    // Other types (bitmap/hll/geo are stored under string/zset) have no direct value shape here.
                    try
                    {
                        var raw = await database.StringGetAsync(key).ConfigureAwait(false);
                        return raw.IsNullOrEmpty ? null : Truncate(raw.ToString(), StringValueLengthCap);
                    }
                    catch (RedisException)
                    {
                        return "(unsupported type)";
                    }
            }
        }
        catch (RedisException)
        {
            return null;
        }
    }

    private static string? RenderArray(string[] rendered, long originalLength)
    {
        if (originalLength == 0)
        {
            return null;
        }

        var text = "[\"" + string.Join("\", \"", rendered) + "\"]";
        return originalLength > ValueElementCap ? text[..^1] + ", ...]" : text;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";
}
