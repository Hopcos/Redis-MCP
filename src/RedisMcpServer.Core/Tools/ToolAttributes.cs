using System.ComponentModel;

namespace RedisMcpServer.Core.Tools;

/// <summary>
/// Marks a method as an MCP/REST tool. Tool methods are instance methods on classes marked
/// with <see cref="McpToolGroupAttribute"/>; parameters carry <see cref="DescriptionAttribute"/>
/// (descriptions shown in the schema) and optional <see cref="ToolParamEnumAttribute"/>.
/// The last parameter must be <c>CancellationToken ct = default</c> (excluded from the schema).
/// The method must return <c>Task&lt;object?&gt;</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class McpToolAttribute : Attribute
{
    /// <summary>Explicit tool name (kebab/snake case). Falls back to the method name.</summary>
    public string? Name { get; init; }

    /// <summary>Detailed tool description; appended with the environment block at runtime.</summary>
    public string Description { get; init; } = string.Empty;
}

/// <summary>Marks a class that groups several tools (used for documentation sections).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class McpToolGroupAttribute : Attribute
{
    public McpToolGroupAttribute(string? name = null) => Name = name;

    /// <summary>Optional explicit group name; falls back to the class name.</summary>
    public string? Name { get; init; }
}

/// <summary>Optional whitelist of allowed values for a parameter (surfaced as JSON schema "enum").</summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class ToolParamEnumAttribute : Attribute
{
    public ToolParamEnumAttribute(params string[] values) => Values = values;

    public string[] Values { get; }
}
