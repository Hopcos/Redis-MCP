using System.Text;
using RedisMcpServer.Core.Configuration;
using RedisMcpServer.Core.Tools;
using StackExchange.Redis;

namespace RedisMcpServer.Core.Environment;

/// <summary>
/// Resolves and validates the Redis environments declared in appsettings ("Redis" section).
/// Thread-safe and immutable after construction.
/// </summary>
public interface IRedisEnvironmentResolver
{
    /// <summary>All configured environments, in declaration order.</summary>
    IReadOnlyList<RedisEnvironment> Environments { get; }

    /// <summary>The first configured environment, used when no "environment" argument is supplied.</summary>
    RedisEnvironment Default { get; }

    /// <summary>
    /// Resolves an environment by name (case-insensitive). <c>null</c>/empty/whitespace selects the default.
    /// Throws <see cref="UnknownEnvironmentException"/> for unknown names.
    /// </summary>
    RedisEnvironment Resolve(string? name);

    /// <summary>
    /// Suffix appended to every tool description: lists each environment with its ToolDescription
    /// and explains the "environment" parameter.
    /// </summary>
    string BuildToolDescriptionSuffix();

    /// <summary>Description text for the "environment" tool parameter, listing available names.</summary>
    string BuildEnvironmentParameterDescription();
}

/// <inheritdoc cref="IRedisEnvironmentResolver"/>
public sealed class RedisEnvironmentResolver : IRedisEnvironmentResolver
{
    private readonly IReadOnlyList<RedisEnvironment> _environments;

    public RedisEnvironmentResolver(IEnumerable<RedisEnvironmentOptions> options)
    {
        var list = options?.ToList() ?? [];
        if (list.Count == 0)
        {
            throw new InvalidOperationException(
                "No Redis environments configured. Add at least one entry to the \"Redis\" section in appsettings.json.");
        }

        if (list.Any(o => string.IsNullOrWhiteSpace(o.Name)))
        {
            throw new InvalidOperationException("Every Redis environment entry must define a non-empty \"Name\".");
        }

        var duplicates = list
            .GroupBy(o => o.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate Redis environment names (case-insensitive): {string.Join(", ", duplicates)}.");
        }

        var resolved = new List<RedisEnvironment>(list.Count);
        foreach (var o in list)
        {
            if (string.IsNullOrWhiteSpace(o.Connection))
            {
                throw new InvalidOperationException(
                    $"Redis environment '{o.Name}' has no \"Connection\" string configured.");
            }

            ConfigurationOptions configuration;
            try
            {
                configuration = ConfigurationOptions.Parse(o.Connection.Trim());
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Redis environment '{o.Name}' has an invalid \"Connection\" string: {ex.Message}", ex);
            }

            resolved.Add(RedisEnvironment.Create(
                o.Name.Trim(), o.ToolDescription?.Trim() ?? string.Empty, o.Connection.Trim(), configuration));
        }

        _environments = resolved;
    }

    public IReadOnlyList<RedisEnvironment> Environments => _environments;

    public RedisEnvironment Default => _environments[0];

    public RedisEnvironment Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Default;
        }

        var match = _environments.FirstOrDefault(e =>
            string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw new UnknownEnvironmentException(name, _environments.Select(e => e.Name));
        }

        return match;
    }

    public string BuildToolDescriptionSuffix()
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("**Environments (from appsettings \"Redis\" section):**");
        foreach (var env in _environments)
        {
            sb.AppendLine($"- `{env.Name}` — {env.DisplayDescription}");
        }

        sb.AppendLine();
        sb.Append("Pass the `environment` parameter to target a specific Redis instance; "
            + "when omitted it defaults to `" + Default.Name + "`.");
        return sb.ToString();
    }

    public string BuildEnvironmentParameterDescription()
        => $"Redis environment name. Available: {string.Join(", ", _environments.Select(e => e.Name))} "
            + $"(omit to use the default: {Default.Name}).";
}
