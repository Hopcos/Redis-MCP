namespace RedisMcpServer.Core.Configuration;

/// <summary>
/// Strong-typed configuration for one Redis environment, bound from the "Redis" array
/// in appsettings.json (e.g. Name / Connection / ToolDescription).
/// </summary>
public sealed class RedisEnvironmentOptions
{
    /// <summary>
    /// Display name used to select this environment from the "environment" tool parameter,
    /// e.g. "Stage". Must be unique (case-insensitive) across the whole list.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// StackExchange.Redis connection string, e.g.
    /// "redis.example.local:6379,allowAdmin=false,defaultDatabase=6,password=secret".
    /// </summary>
    public string Connection { get; set; } = string.Empty;

    /// <summary>
    /// Extra human-readable text about this environment. It is injected verbatim into
    /// every Tool description and into the global MCP Instructions usage guide,
    /// e.g. "Stage环境的Name参数值为Stage".
    /// </summary>
    public string ToolDescription { get; set; } = string.Empty;
}

/// <summary>
/// Options bound from the root configuration. The "Redis" property maps 1:1 to the
/// top-level "Redis" section of appsettings.json.
/// </summary>
public sealed class RedisOptions
{
    /// <summary>The configured Redis environments, in declaration order.</summary>
    public List<RedisEnvironmentOptions> Redis { get; set; } = [];
}
