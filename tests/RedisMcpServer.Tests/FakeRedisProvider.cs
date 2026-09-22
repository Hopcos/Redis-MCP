using RedisMcpServer.Core.Configuration;
using RedisMcpServer.Core.Environment;
using RedisMcpServer.Core.Redis;
using RedisMcpServer.Core.Tools;
using StackExchange.Redis;

namespace RedisMcpServer.Tests;

/// <summary>Fake provider that resolves the environment and runs handlers without any Redis IO.</summary>
internal sealed class FakeRedisProvider(IRedisEnvironmentResolver resolver) : IRedisConnectionProvider
{
    public Task<object?> ExecuteAsync(
        string? environmentName,
        Func<RedisToolContext, CancellationToken, ValueTask<object?>> handler,
        CancellationToken cancellationToken)
    {
        var environment = resolver.Resolve(environmentName);
        var context = new RedisToolContext(environment, null!, null!, null!);
        return handler(context, cancellationToken).AsTask();
    }

    public Task<ConnectionMultiplexer> GetMultiplexerAsync(string? environmentName, CancellationToken cancellationToken)
        => throw new NotSupportedException("No real Redis in unit tests.");

    public Task<IReadOnlyList<ConnectionStatus>> GetStatusSnapshotAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ConnectionStatus>>(
            resolver.Environments.Select(e => new ConnectionStatus(e.Name, false, "NotConnected", null)).ToList());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
