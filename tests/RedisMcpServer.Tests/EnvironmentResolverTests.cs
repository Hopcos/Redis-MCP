using RedisMcpServer.Core.Configuration;
using RedisMcpServer.Core.Environment;
using RedisMcpServer.Core.Tools;

namespace RedisMcpServer.Tests;

public class EnvironmentResolverTests
{
    private const string StageToolDescription = "Stage环境的Name参数值为Stage";
    private const string SampleConnection = "xxx.yyy.local:6379,allowAdmin=false,defaultDatabase=6,password=secret";

    private static RedisOptions Options(params RedisEnvironmentOptions[] environments)
        => new() { Redis = environments.ToList() };

    private static RedisEnvironmentResolver BuildResolver(params RedisEnvironmentOptions[] environments)
        => new(Options(environments).Redis);

    [Fact]
    public void ParsesUserSample_WithToolDescription_AndHiddenPassword()
    {
        var resolver = BuildResolver(new RedisEnvironmentOptions
        {
            Name = "Stage",
            ToolDescription = StageToolDescription,
            Connection = SampleConnection,
        });

        var environment = Assert.Single(resolver.Environments);
        Assert.Equal("Stage", environment.Name);
        Assert.Equal(StageToolDescription, environment.ToolDescription);
        Assert.Equal(StageToolDescription, environment.DisplayDescription);
        Assert.Equal(6, environment.DefaultDatabase);
        Assert.True(environment.HasPassword);
        Assert.Equal("xxx.yyy.local", environment.Host);
        Assert.Equal(6379, environment.Port);
    }

    [Fact]
    public void DisplayDescription_FallsBackToName_WhenToolDescriptionIsEmpty()
    {
        var resolver = BuildResolver(new RedisEnvironmentOptions
        {
            Name = "Local",
            Connection = "localhost:6379,defaultDatabase=0",
        });

        var environment = Assert.Single(resolver.Environments);
        Assert.Equal("Local", environment.DisplayDescription);
        Assert.False(environment.HasPassword);
    }

    [Fact]
    public void Resolve_IsCaseInsensitive_AndNullSelectsDefault()
    {
        var resolver = BuildResolver(
            new RedisEnvironmentOptions { Name = "Stage", Connection = SampleConnection },
            new RedisEnvironmentOptions { Name = "Local", Connection = "localhost:6379" });

        Assert.Equal("Stage", resolver.Default.Name);
        Assert.Equal("Stage", resolver.Resolve(null).Name);
        Assert.Equal("Stage", resolver.Resolve("  ").Name);
        Assert.Equal("Local", resolver.Resolve("LOCAL").Name);
    }

    [Fact]
    public void Resolve_UnknownEnvironment_ThrowsWithAvailableList()
    {
        var resolver = BuildResolver(new RedisEnvironmentOptions { Name = "Stage", Connection = SampleConnection });

        var ex = Assert.Throws<UnknownEnvironmentException>(() => resolver.Resolve("Prod"));
        Assert.Contains("Stage", ex.Message);
    }

    [Fact]
    public void EmptyConfiguration_Throws()
        => Assert.Throws<InvalidOperationException>(() => BuildResolver());

    [Fact]
    public void MissingConnection_Throws()
        => Assert.Throws<InvalidOperationException>(() => BuildResolver(
            new RedisEnvironmentOptions { Name = "Stage", Connection = string.Empty }));

    [Fact]
    public void DuplicateNames_Throws()
        => Assert.Throws<InvalidOperationException>(() => BuildResolver(
            new RedisEnvironmentOptions { Name = "Stage", Connection = SampleConnection },
            new RedisEnvironmentOptions { Name = "stage", Connection = "localhost:6379" }));

    [Fact]
    public void InvalidConnectionString_Throws()
        => Assert.Throws<InvalidOperationException>(() => BuildResolver(
            new RedisEnvironmentOptions { Name = "Stage", Connection = "localhost:6379,allowAdmin=maybe" }));

    [Fact]
    public void EnvironmentDescriptions_AreSurfacedInToolMetadata()
    {
        var resolver = BuildResolver(new RedisEnvironmentOptions
        {
            Name = "Stage",
            ToolDescription = StageToolDescription,
            Connection = SampleConnection,
        });

        Assert.Contains(StageToolDescription, resolver.BuildToolDescriptionSuffix());
        Assert.Contains("Stage", resolver.BuildEnvironmentParameterDescription());
    }
}
