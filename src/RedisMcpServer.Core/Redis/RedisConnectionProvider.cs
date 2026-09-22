using System.Collections.Concurrent;
using RedisMcpServer.Core.Configuration;
using RedisMcpServer.Core.Environment;
using RedisMcpServer.Core.Tools;
using StackExchange.Redis;

namespace RedisMcpServer.Core.Redis;

/// <inheritdoc cref="IRedisConnectionProvider"/>
/// <remarks>
/// Thread-safety: one <see cref="ConnectionMultiplexer"/> per environment is created lazily and
/// shared by all concurrent requests (StackExchange.Redis multiplexers are thread-safe).
/// The per-environment creation is guarded by <see cref="Lazy{T}"/> with
/// <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/>, so concurrent callers await the
/// same connect task instead of connecting N times. Failed connects evict the cache entry so a
/// later call can retry.
/// </remarks>
public sealed class RedisConnectionProvider : IRedisConnectionProvider
{
    private readonly IRedisEnvironmentResolver _resolver;
    private readonly ConcurrentDictionary<string, Lazy<Task<ConnectionMultiplexer>>> _multiplexers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private bool _disposed;

    public RedisConnectionProvider(IRedisEnvironmentResolver resolver)
    {
        _resolver = resolver;
    }

    public async Task<object?> ExecuteAsync(
        string? environmentName,
        Func<RedisToolContext, CancellationToken, ValueTask<object?>> handler,
        CancellationToken cancellationToken)
    {
        var environment = _resolver.Resolve(environmentName);
        var multiplexer = await GetMultiplexerCoreAsync(environment, cancellationToken).ConfigureAwait(false);

        var context = new RedisToolContext(
            environment,
            multiplexer,
            multiplexer.GetDatabase(),
            multiplexer.GetServer(multiplexer.GetEndPoints(configuredOnly: true)[0]));

        try
        {
            return await handler(context, cancellationToken).ConfigureAwait(false);
        }
        catch (RedisException ex)
        {
            throw new ToolExecutionException($"Redis error on environment '{environment.Name}': {ex.Message}", ex)
            {
                ErrorCode = (ex as RedisServerException)?.Message?.Split(' ', 2).FirstOrDefault(),
            };
        }
        catch (TimeoutException ex)
        {
            throw new ToolExecutionException($"Redis timeout on environment '{environment.Name}': {ex.Message}", ex);
        }
        catch (IOException ex)
        {
            throw new ToolExecutionException($"Redis I/O error on environment '{environment.Name}': {ex.Message}", ex);
        }
    }

    public Task<ConnectionMultiplexer> GetMultiplexerAsync(string? environmentName, CancellationToken cancellationToken)
        => GetMultiplexerCoreAsync(_resolver.Resolve(environmentName), cancellationToken);

    private async Task<ConnectionMultiplexer> GetMultiplexerCoreAsync(RedisEnvironment environment, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var lazy = _multiplexers.GetOrAdd(environment.Name,
            _ => new Lazy<Task<ConnectionMultiplexer>>(
                () => ConnectAsync(environment, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // A failed connect is cached inside the Lazy<Task>; evict it so future calls retry.
            _multiplexers.TryRemove(environment.Name, out _);
            throw;
        }
    }

    private static async Task<ConnectionMultiplexer> ConnectAsync(RedisEnvironment environment, CancellationToken cancellationToken)
    {
        var configuration = environment.Configuration.Clone();
        // Read-oriented defaults: keep trying to reconnect without killing the API call.
        configuration.AbortOnConnectFail = false;
        configuration.ConnectRetry = 3;
        configuration.ConnectTimeout = 10_000;
        configuration.SyncTimeout = 10_000;

        // StackExchange.Redis blocks INFO / CONFIG GET / CLIENT LIST / SLOWLOG / CLUSTER * on the
        // client side when AllowAdmin is false, even though Redis itself lets any client run these
        // read queries (redis-cli / RDM do). This server's tool layer guarantees read-only behavior
        // (it only ever issues query commands), so the client admin bit is enabled purely to make
        // those RDM-equivalent inspections possible. No admin/write command is ever sent.
        configuration.AllowAdmin = true;

        return await ConnectionMultiplexer.ConnectAsync(configuration).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ConnectionStatus>> GetStatusSnapshotAsync(CancellationToken cancellationToken)
    {
        var statuses = new List<ConnectionStatus>(_resolver.Environments.Count);
        foreach (var environment in _resolver.Environments)
        {
            try
            {
                var multiplexer = await GetMultiplexerCoreAsync(environment, cancellationToken).ConfigureAwait(false);
                statuses.Add(new ConnectionStatus(environment.Name, multiplexer.IsConnected, multiplexer.GetStatus(), null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                statuses.Add(new ConnectionStatus(environment.Name, false, "NotConnected", ex.Message));
            }
        }

        return statuses;
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        foreach (var lazy in _multiplexers.Values)
        {
            try
            {
                if (lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully)
                {
                    await lazy.Value.Result.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                // Best-effort shutdown.
            }
        }

        _multiplexers.Clear();
    }
}
