namespace RedisMcpServer.Core.Tools;

/// <summary>Registry of every discovered tool, indexed by name (case-insensitive).</summary>
public interface IToolRegistry
{
    /// <summary>All tools, ordered by group then name.</summary>
    IReadOnlyList<ITool> Tools { get; }

    /// <summary>Tries to resolve a tool by name (case-insensitive).</summary>
    bool TryGet(string name, out ITool tool);

    /// <summary>Resolves a tool by name or throws <see cref="ToolNotFoundException"/>.</summary>
    ITool GetRequired(string name);
}

/// <inheritdoc cref="IToolRegistry"/>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly IReadOnlyList<ITool> _tools;
    private readonly IReadOnlyDictionary<string, ITool> _byName;

    public ToolRegistry(IEnumerable<ITool> tools)
    {
        _tools = tools
            .DistinctBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var duplicates = tools
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException($"Duplicate tool names (case-insensitive): {string.Join(", ", duplicates)}.");
        }

        _byName = _tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ITool> Tools => _tools;

    public bool TryGet(string name, out ITool tool) => _byName.TryGetValue(name, out tool!);

    public ITool GetRequired(string name)
        => TryGet(name, out var tool)
            ? tool
            : throw new ToolNotFoundException(name, _tools.Select(t => t.Name));
}
