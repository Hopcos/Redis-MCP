using RedisMcpServer.Core.Redis;

namespace RedisMcpServer.Tools;

/// <summary>Shared documentation snippets used on tool and parameter descriptions.</summary>
public static class ToolDocs
{
    public const string Environment =
        "Redis environment name (case-insensitive). Available environments are listed by the "
        + "list_environments tool / appsettings \"Redis\" section; defaults to the first configured one. "
        + "Redis 环境名称（不区分大小写），省略时使用第一个配置的环境。";

    public const string Key = "The key name. 键名。";
    public const string Pattern = "Glob-style key pattern, e.g. \"user:*\". 键匹配模式。";
    public const string KeyTypeFilter =
        "Optional type filter for key enumeration: string|hash|list|set|zset|stream (Redis SCAN TYPE). 按类型过滤。";
}

/// <summary>
/// Base class for tool groups: hands every tool the shared async Redis connection provider.
/// Tool classes are discovered via the <c>McpToolGroup</c> attribute and instantiated by DI.
/// </summary>
public abstract class RedisToolBase(IRedisConnectionProvider redis)
{
    protected IRedisConnectionProvider Redis { get; } = redis;

    /// <summary>Runs a handler against the resolved environment (see <see cref="IRedisConnectionProvider.ExecuteAsync"/>).</summary>
    protected Task<object?> RunAsync(
        string? environment,
        Func<RedisToolContext, CancellationToken, ValueTask<object?>> handler,
        CancellationToken ct)
        => Redis.ExecuteAsync(environment, handler, ct);
}
