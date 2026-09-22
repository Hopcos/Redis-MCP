using RedisMcpServer.Core.Configuration;
using StackExchange.Redis;

namespace RedisMcpServer.Core.Redis;

/// <summary>
/// Execution context handed to every tool handler: the resolved environment plus the
/// multiplexed <see cref="IDatabase"/> / <see cref="IServer"/> handles.
/// </summary>
public sealed class RedisToolContext
{
    internal RedisToolContext(RedisEnvironment environment, ConnectionMultiplexer multiplexer, IDatabase database, IServer server)
    {
        Environment = environment;
        Multiplexer = multiplexer;
        Database = database;
        Server = server;
    }

    /// <summary>The resolved environment this context targets.</summary>
    public RedisEnvironment Environment { get; }

    /// <summary>The shared multiplexer for this environment.</summary>
    public ConnectionMultiplexer Multiplexer { get; }

    /// <summary>Logical database handle (default database from the connection string).</summary>
    public IDatabase Database { get; }

    /// <summary>Server handle for the first endpoint (INFO/CONFIG/CLIENT/SLOWLOG style commands).</summary>
    public IServer Server { get; }

    public string EnvironmentName => Environment.Name;
}
