namespace RedisMcpServer.Core.Browse;

/// <summary>
/// Read-only browsing over Redis databases, powering the front-end Redis explorer UI.
/// All methods are protocol-neutral (no MCP/REST dependency) and fully async.
/// </summary>
public interface IRedisBrowseService
{
    /// <summary>Returns every logical database of an environment with its key count (DBSIZE) and ephemeral key count (expires).</summary>
    Task<IReadOnlyList<DatabaseSummary>> ListDatabasesAsync(string? environment, CancellationToken cancellationToken);

    /// <summary>Counts keys per data type in one database via a full non-blocking SCAN.</summary>
    Task<TypeSummary> CountTypesAsync(string? environment, int? db, CancellationToken cancellationToken);

    /// <summary>
    /// Paginated key/value query over one database with optional db/type/key/value filters,
    /// sorted by the estimated creation time (see README: Redis stores no real creation timestamp).
    /// </summary>
    Task<QueryResult> QueryAsync(KeyQuery query, CancellationToken cancellationToken);
}
