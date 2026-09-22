using System.Reflection;

namespace RedisMcpServer.Core.Tools;

/// <summary>
/// A protocol-neutral tool definition. The MCP adapter and the REST API both invoke tools
/// through <see cref="HandlerMethod"/> (bound to <see cref="Target"/>), so there is exactly
/// one implementation path for every tool.
/// </summary>
public interface ITool
{
    /// <summary>Unique tool name used by MCP <c>tools/call</c> and REST <c>/api/tools/&lt;name&gt;/invoke</c>.</summary>
    string Name { get; }

    /// <summary>Detailed description; the environment block is appended by the enrichment layer.</summary>
    string Description { get; }

    /// <summary>Documentation group (e.g. "Keys", "Strings", "Server").</summary>
    string Group { get; }

    /// <summary>Declared input parameters (in method order).</summary>
    IReadOnlyList<ToolParameter> Parameters { get; }

    /// <summary>The method implementing the tool.</summary>
    MethodInfo HandlerMethod { get; }

    /// <summary>Instance the handler method is invoked on (DI-resolved tool class).</summary>
    object Target { get; }
}

/// <summary>Default immutable implementation of <see cref="ITool"/>.</summary>
public sealed class ToolDefinition : ITool
{
    public ToolDefinition(
        string name,
        string description,
        string group,
        IReadOnlyList<ToolParameter> parameters,
        MethodInfo handlerMethod,
        object target)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? string.Empty;
        Group = string.IsNullOrWhiteSpace(group) ? "Misc" : group;
        Parameters = parameters ?? [];
        HandlerMethod = handlerMethod ?? throw new ArgumentNullException(nameof(handlerMethod));
        Target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public string Name { get; }
    public string Description { get; }
    public string Group { get; }
    public IReadOnlyList<ToolParameter> Parameters { get; }
    public MethodInfo HandlerMethod { get; }
    public object Target { get; }

    public override string ToString() => $"{Name} ({Group})";
}
