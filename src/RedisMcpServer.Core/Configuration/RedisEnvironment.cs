using System.Net;
using StackExchange.Redis;

namespace RedisMcpServer.Core.Configuration;

/// <summary>
/// An immutable, validated representation of one configured Redis environment.
/// Created by <see cref="Environment.RedisEnvironmentResolver"/>.
/// </summary>
public sealed record RedisEnvironment
{
    private RedisEnvironment(
        string name,
        string toolDescription,
        string connectionString,
        ConfigurationOptions configuration)
    {
        Name = name;
        ToolDescription = toolDescription;
        ConnectionString = connectionString;
        Configuration = configuration;
        DefaultDatabase = configuration.DefaultDatabase ?? 0;
        (Host, Port) = ResolvePrimaryEndpoint(configuration);
    }

    /// <summary>Environment display name, e.g. "Stage".</summary>
    public string Name { get; }

    /// <summary>Raw ToolDescription text from configuration (may be empty).</summary>
    public string ToolDescription { get; }

    /// <summary>Original connection string (never surfaced through the REST/MCP APIs).</summary>
    public string ConnectionString { get; }

    /// <summary>Parsed StackExchange.Redis options used to create the multiplexer.</summary>
    public ConfigurationOptions Configuration { get; }

    /// <summary>Default logical database inferred from the connection string (defaults to 0).</summary>
    public int DefaultDatabase { get; }

    /// <summary>Host of the primary endpoint, for display purposes only.</summary>
    public string Host { get; }

    /// <summary>Port of the primary endpoint, for display purposes only.</summary>
    public int Port { get; }

    /// <summary>Whether the connection string carries a password. Never exposes the password itself.</summary>
    public bool HasPassword => !string.IsNullOrEmpty(Configuration.Password);

    /// <summary>
    /// Human-readable description used in tool metadata: ToolDescription when present, otherwise the name.
    /// </summary>
    public string DisplayDescription => string.IsNullOrWhiteSpace(ToolDescription) ? Name : ToolDescription;

    internal static RedisEnvironment Create(
        string name, string toolDescription, string connectionString, ConfigurationOptions configuration)
        => new(name, toolDescription, connectionString, configuration);

    private static (string Host, int Port) ResolvePrimaryEndpoint(ConfigurationOptions configuration)
    {
        var endpoint = configuration.EndPoints.FirstOrDefault();
        if (endpoint is DnsEndPoint dns)
        {
            return (dns.Host, dns.Port);
        }

        if (endpoint is IPEndPoint ip)
        {
            return (ip.Address.ToString(), ip.Port);
        }

        return ("unknown", 6379);
    }
}
