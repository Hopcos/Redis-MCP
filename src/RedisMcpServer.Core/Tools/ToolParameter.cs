namespace RedisMcpServer.Core.Tools;

/// <summary>
/// Declarative description of one tool input parameter, used to drive the MCP input schema,
/// the REST API documentation and the global instructions guide.
/// </summary>
public sealed record ToolParameter(
    string Name,
    ToolParameterKind Kind,
    string Description,
    bool IsRequired,
    object? DefaultValue = null,
    string[]? AllowedValues = null);
