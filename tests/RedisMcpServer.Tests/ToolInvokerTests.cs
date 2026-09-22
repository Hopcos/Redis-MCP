using System.ComponentModel;
using System.Text.Json;
using RedisMcpServer.Core.Tools;

namespace RedisMcpServer.Tests;

/// <summary>An in-memory tool used to test argument binding/coercion without Redis.</summary>
[McpToolGroup("Test")]
public sealed class EchoTools
{
    [McpTool(Name = "echo_value", Description = "Echoes the given arguments back.")]
    public Task<object?> EchoAsync(
        [Description("Text to echo.")] string text,
        [Description("How many times.")] long count = 1,
        [Description("Optional mode.")]
        [ToolParamEnum("a", "b", "c")]
        string? mode = null,
        CancellationToken ct = default)
        => Task.FromResult<object?>(new { text, count, mode });

    [McpTool(Name = "echo_array", Description = "Echoes an array argument.")]
    public Task<object?> EchoArrayAsync(
        [Description("Items.")] string[] items,
        CancellationToken ct = default)
        => Task.FromResult<object?>(new { items });

    [McpTool(Name = "echo_int", Description = "Accepts an int parameter (coercion must adapt long -> int).")]
    public Task<object?> EchoIntAsync(
        [Description("Number.")] int number,
        CancellationToken ct = default)
        => Task.FromResult<object?>(new { number });
}

public class ToolInvokerTests
{
    private static readonly IToolRegistry Registry = BuildRegistry();

    private static IToolRegistry BuildRegistry()
    {
        var discovered = ToolDiscovery.Scan(typeof(EchoTools).Assembly);
        var tools = ToolDiscovery.Build(discovered, type => Activator.CreateInstance(type)!);
        return new ToolRegistry(tools);
    }

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement;

    [Fact]
    public async Task InvokesWithCoercedArguments()
    {
        var invoker = new ToolInvoker(Registry);
        var arguments = new Dictionary<string, JsonElement>
        {
            ["text"] = Json("\"hello\""),
            ["count"] = Json("3"), // numeric string accepted for integer parameters
            ["mode"] = Json("\"b\""),
        };

        var result = await invoker.InvokeAsync("echo_value", null, arguments, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotNull(result.Data);
        Assert.Contains("hello", result.Text);
        Assert.True(result.DurationMs >= 0);
    }

    [Fact]
    public async Task AppliesDefaultValues_WhenOptionalParametersAreOmitted()
    {
        var invoker = new ToolInvoker(Registry);
        var arguments = new Dictionary<string, JsonElement> { ["text"] = Json("\"hi\"") };

        var result = await invoker.InvokeAsync("echo_value", null, arguments, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("\"count\": 1", result.Text);
    }

    [Fact]
    public async Task MissingRequiredParameter_ReturnsIsErrorWithToolName()
    {
        var invoker = new ToolInvoker(Registry);
        var result = await invoker.InvokeAsync("echo_value", null, new Dictionary<string, JsonElement>(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("Missing required parameter 'text'", result.Text);
        Assert.IsType<string>(result.Error);
    }

    [Fact]
    public async Task UnknownTool_ReturnsIsError()
    {
        var invoker = new ToolInvoker(Registry);
        var result = await invoker.InvokeAsync("does_not_exist", null, new Dictionary<string, JsonElement>(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("does_not_exist", result.Text);
    }

    [Fact]
    public async Task ArrayArguments_AreBoundToStringArrays()
    {
        var invoker = new ToolInvoker(Registry);
        var arguments = new Dictionary<string, JsonElement>
        {
            ["items"] = Json("[\"a\", \"b\"]"),
        };

        var result = await invoker.InvokeAsync("echo_array", null, arguments, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("\"items\": [", result.Text);
    }

    [Fact]
    public async Task IntegerArguments_AreAdaptedToDeclaredParameterType()
    {
        var invoker = new ToolInvoker(Registry);
        var arguments = new Dictionary<string, JsonElement> { ["number"] = Json("7") };

        var result = await invoker.InvokeAsync("echo_int", null, arguments, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("\"number\": 7", result.Text);
    }

    [Fact]
    public void Coerce_NumericStrings_AndRejectsBogusInput()
    {
        var integerParam = new ToolParameter("count", ToolParameterKind.Integer, "n", true);
        Assert.Equal(42L, ToolInvoker.Coerce(integerParam, Json("42")));
        Assert.Equal(42L, ToolInvoker.Coerce(integerParam, Json("\"0042\"")));

        Assert.Throws<ToolExecutionException>(() => ToolInvoker.Coerce(integerParam, Json("\"abc\"")));
        Assert.Throws<ToolExecutionException>(() => ToolInvoker.Coerce(integerParam, Json("true")));

        var booleanParam = new ToolParameter("flag", ToolParameterKind.Boolean, "f", true);
        Assert.True((bool)ToolInvoker.Coerce(booleanParam, Json("\"yes\""))!);
        Assert.False((bool)ToolInvoker.Coerce(booleanParam, Json("false"))!);
        Assert.False((bool)ToolInvoker.Coerce(booleanParam, Json("\"no\""))!);
        Assert.Throws<ToolExecutionException>(() => ToolInvoker.Coerce(booleanParam, Json("\"maybe\"")));
    }
}
