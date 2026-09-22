using RedisMcpServer.Core.Configuration;
using StackExchange.Redis;

namespace RedisMcpServer.Core.Redis;

/// <summary>
/// Per-environment, thread-safe access to Redis. Shares one ConnectionMultiplexer per
/// configured environment (multiplexers are designed to be used concurrently from many
/// threads/requests), so all interaction with a Redis server happens asynchronously
/// without blocking the thread pool.
/// </summary>
public interface IRedisConnectionProvider : IAsyncDisposable
{
    /// <summary>
    /// Executes a read handler against the resolved environment. The handler receives a
    /// <see cref="RedisToolContext"/> exposing the multiplexed database and server.
    /// </summary>
    Task<object?> ExecuteAsync(
        string? environmentName,
        Func<RedisToolContext, CancellationToken, ValueTask<object?>> handler,
        CancellationToken cancellationToken);

    /// <summary>Raw access to the multiplexer for the resolved environment (used by integration plumbing).</summary>
    Task<ConnectionMultiplexer> GetMultiplexerAsync(string? environmentName, CancellationToken cancellationToken);

    /// <summary>Current connectivity status for every resolved environment (never throws).</summary>
    Task<IReadOnlyList<ConnectionStatus>> GetStatusSnapshotAsync(CancellationToken cancellationToken);
}

/// <summary>Connectivity status of one environment, for diagnostics.</summary>
public sealed record ConnectionStatus(string EnvironmentName, bool IsConnected, string Status, string? Error);
