using StackExchange.Redis;

namespace RedisMcpServer.Core.Redis;

/// <summary>
/// Read-only command helpers that eliminate StackExchange.Redis ExecuteAsync overload traps
/// (CommandFlags/CancellationToken must never be passed as raw arguments). All commands issued
/// through these helpers are plain reads with default flags.
/// </summary>
public static class RedisReadExtensions
{
    /// <summary>Executes a read command (e.g. "SCAN", "TTL", "OBJECT") on the database, arguments as object[] (RedisKey/RedisValue/primitive supported).</summary>
    public static Task<RedisResult> ExecuteReadAsync(this IDatabase database, string command, params object?[] args)
        => database.ExecuteAsync(command, args!, CommandFlags.None);

    /// <summary>Executes a read command on the server (e.g. "CLUSTER INFO", "LATENCY LATEST").</summary>
    public static Task<RedisResult> ExecuteReadAsync(this IServer server, string command, params object?[] args)
        => server.ExecuteAsync(command, args!, CommandFlags.None);
}
