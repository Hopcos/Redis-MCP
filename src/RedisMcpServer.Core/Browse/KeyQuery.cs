using RedisMcpServer.Core.Tools;

namespace RedisMcpServer.Core.Browse;

/// <summary>Query options for the paginated key/value browse API. All filters are optional.</summary>
public sealed record KeyQuery
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 500;
    private const string OrderAscending = "asc";
    private const string OrderDescending = "desc";

    /// <summary>Redis environment name; when omitted the default environment is used.</summary>
    public string? Environment { get; init; }

    /// <summary>Logical database index (0..N); when omitted the environment's default database is used.</summary>
    public int? Db { get; init; }

    /// <summary>Exact data type filter (string/hash/list/set/zset/stream/...). Optional.</summary>
    public string? Type { get; init; }

    /// <summary>Case-insensitive substring matched against the key. Optional.</summary>
    public string? KeyPattern { get; init; }

    /// <summary>Case-insensitive substring matched against the rendered value. Optional.</summary>
    public string? ValuePattern { get; init; }

    /// <summary>Sort order of the time estimate: "asc" or "desc" (default "desc").</summary>
    public string Order { get; init; } = OrderDescending;

    /// <summary>Page size, 1..<see cref="MaxPageSize"/> (default <see cref="DefaultPageSize"/>).</summary>
    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>1-based page number (default 1).</summary>
    public int PageNumber { get; init; } = 1;

    public bool IsDescendingOrder => !string.Equals(Order, OrderAscending, StringComparison.OrdinalIgnoreCase);

    /// <summary>Validates the option values; throws <see cref="ToolExecutionException"/> with a friendly message for the UI.</summary>
    public void Validate()
    {
        if (Db is < 0)
        {
            throw new ToolExecutionException("Parameter 'db' must be >= 0.");
        }

        if (!string.IsNullOrWhiteSpace(Type) && !string.Equals(Type, Type?.Trim(), StringComparison.Ordinal))
        {
            throw new ToolExecutionException("Parameter 'type' must not contain leading/trailing whitespace.");
        }

        if (!string.Equals(Order, OrderAscending, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Order, OrderDescending, StringComparison.OrdinalIgnoreCase))
        {
            throw new ToolExecutionException("Parameter 'order' must be 'asc' or 'desc'.");
        }

        if (PageSize is < 1 or > MaxPageSize)
        {
            throw new ToolExecutionException($"Parameter 'pageSize' must be between 1 and {MaxPageSize}.");
        }

        if (PageNumber < 1)
        {
            throw new ToolExecutionException("Parameter 'pageNumber' must be >= 1.");
        }
    }
}

/// <summary>One logical database: index, live key count and key count with TTL (expires).</summary>
public sealed record DatabaseSummary(int Db, long Keys, long Expires);

/// <summary>Per-type key counts for one database.</summary>
public sealed record TypeSummary(int Db, long Total, IReadOnlyList<TypeCount> Types);

/// <summary>Key count for a single Redis data type.</summary>
public sealed record TypeCount(string Type, long Count);

/// <summary>One key/value row returned by the browse query API.</summary>
public sealed record KeyRow(
    /// <summary>Estimated creation time (now minus OBJECT IDLETIME). Redis stores no real creation
    /// timestamp; on LFU-policy or Redis 6.0 servers OBJECT IDLETIME is unavailable/coarse, so this
    /// is frequently null. See <see cref="LastAccessAt"/> / <see cref="IdleSeconds"/> for raw data.</summary>
    DateTimeOffset? CreatedAt,
    string Type,
    string Key,
    string? Value,
    /// <summary>Remaining lifetime in whole seconds; null means the key never expires (TTL = -1).</summary>
    long? TtlSeconds,
    /// <summary>Seconds since last access (raw OBJECT IDLETIME); null when the command is unavailable.</summary>
    long? IdleSeconds = null,
    /// <summary>Approximate last-access time (now - OBJECT IDLETIME); null when unavailable.</summary>
    DateTimeOffset? LastAccessAt = null,
    /// <summary>LFU access-frequency counter (raw OBJECT FREQ); null on LRU-policy servers.</summary>
    long? AccessFrequency = null);

/// <summary>Paginated result of <see cref="KeyQuery"/>.</summary>
public sealed record QueryResult(
    int PageNumber,
    int PageSize,
    long Total,
    int TotalPages,
    string Environment,
    int Db,
    IReadOnlyList<KeyRow> Items);
