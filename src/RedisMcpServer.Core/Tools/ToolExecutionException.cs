namespace RedisMcpServer.Core.Tools;

/// <summary>
/// Base exception for all user-facing tool failures. The message is safe to surface
/// to MCP/REST clients (no stack traces, no secrets).
/// </summary>
public class ToolExecutionException : Exception
{
    public ToolExecutionException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    /// <summary>Optional Redis error code, e.g. WRONGTYPE, when the failure stems from Redis.</summary>
    public string? ErrorCode { get; init; }
}

/// <summary>
/// Thrown when a caller references a tool name that is not registered.
/// </summary>
public sealed class ToolNotFoundException : ToolExecutionException
{
    public ToolNotFoundException(string toolName, IEnumerable<string> availableTools)
        : base($"Tool '{toolName}' was not found. Available tools: {string.Join(", ", availableTools)}.")
    {
    }
}

/// <summary>
/// Thrown when a caller references an unknown Redis environment name.
/// </summary>
public sealed class UnknownEnvironmentException : ToolExecutionException
{
    public UnknownEnvironmentException(string environmentName, IEnumerable<string> availableEnvironments)
        : base(
            $"Unknown Redis environment '{environmentName}'. Configured environments: {string.Join(", ", availableEnvironments)}. " +
            "Use the 'list_environments' tool to inspect them.")
    {
    }
}
