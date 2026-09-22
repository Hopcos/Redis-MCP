using System.Globalization;
using System.Reflection;
using System.Text.Json;
using RedisMcpServer.Core.Json;

namespace RedisMcpServer.Core.Tools;

/// <summary>Result of one tool invocation, shared by the MCP adapter and the REST API.</summary>
public sealed record ToolInvokeResult(
    bool IsError,
    object? Data,
    string Text,
    long DurationMs,
    string? Error = null)
{
    public static ToolInvokeResult Success(object? data, long durationMs)
    {
        var dataWithDuration = new { data, durationMs };
        return new ToolInvokeResult(false, data, JsonDefaults.ToText(dataWithDuration), durationMs);
    }

    public static ToolInvokeResult Failure(string error, long durationMs)
        => new(true, null, JsonDefaults.ToText(new { error, durationMs }), durationMs, error);
}

/// <summary>
/// Executes tools by name with argument binding/coercion. Single code path used by both the
/// MCP CallTool handler and the REST invoke endpoint.
/// </summary>
public interface IToolInvoker
{
    Task<ToolInvokeResult> InvokeAsync(
        string toolName,
        string? environment,
        IReadOnlyDictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken);
}

/// <inheritdoc cref="IToolInvoker"/>
public sealed class ToolInvoker : IToolInvoker
{
    private readonly IToolRegistry _registry;

    public ToolInvoker(IToolRegistry registry)
    {
        _registry = registry;
    }

    public async Task<ToolInvokeResult> InvokeAsync(
        string toolName,
        string? environment,
        IReadOnlyDictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var tool = _registry.GetRequired(toolName);
            var bound = BindArguments(tool, environment, arguments);
            var result = await InvokeHandlerAsync(tool, bound, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            return ToolInvokeResult.Success(result, sw.ElapsedMilliseconds);
        }
        catch (ToolExecutionException ex)
        {
            sw.Stop();
            return ToolInvokeResult.Failure(ex.Message, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return ToolInvokeResult.Failure(
                $"Internal error invoking tool '{toolName}': {ex.Message}", sw.ElapsedMilliseconds);
        }
    }

    private static object?[] BindArguments(ITool tool, string? environment, IReadOnlyDictionary<string, JsonElement> arguments)
    {
        var methodHasCancellationToken = tool.HandlerMethod
            .GetParameters()
            .Any(p => p.ParameterType == typeof(CancellationToken));

        var invokeArgs = new List<object?>(tool.Parameters.Count + 1);
        foreach (var parameter in tool.Parameters)
        {
            if (string.Equals(parameter.Name, "environment", StringComparison.OrdinalIgnoreCase))
            {
                invokeArgs.Add(environment);
                continue;
            }

            if (arguments.TryGetValue(parameter.Name, out var element))
            {
                invokeArgs.Add(Coerce(parameter, element));
            }
            else if (!parameter.IsRequired)
            {
                invokeArgs.Add(parameter.DefaultValue);
            }
            else
            {
                throw new ToolExecutionException(
                    $"Missing required parameter '{parameter.Name}' for tool '{tool.Name}'.");
            }
        }

        if (methodHasCancellationToken)
        {
            invokeArgs.Add((object?)default(CancellationToken)); // replaced below with the real token
        }

        return invokeArgs.ToArray();
    }

    private static async Task<object?> InvokeHandlerAsync(ITool tool, object?[] args, CancellationToken cancellationToken)
    {
        var parameters = tool.HandlerMethod.GetParameters();
        for (var i = 0; i < args.Length && i < parameters.Length; i++)
        {
            if (parameters[i].ParameterType == typeof(CancellationToken))
            {
                args[i] = cancellationToken;
            }
            else
            {
                // Coerce() normalizes JSON to long/double, but the method may declare int/float —
                // reflection Invoke refuses Int64->Int32, so adapt to the declared parameter type.
                args[i] = AdaptToParameterType(args[i], parameters[i].ParameterType);
            }
        }

        object? raw;
        try
        {
            raw = tool.HandlerMethod.Invoke(tool.Target, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw Unwrap(ex.InnerException, tool.Name);
        }

        return await AwaitResultAsync(raw, tool.Name, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Converts a coerced value to the CLR type declared by the tool method parameter.</summary>
    private static object? AdaptToParameterType(object? value, Type targetType)
    {
        if (value is null || value.GetType() == targetType || targetType.IsInstanceOfType(value))
        {
            return value;
        }

        if (targetType == typeof(string))
        {
            return value switch
            {
                string s => s,
                _ => value.ToString() ?? string.Empty,
            };
        }

        if (targetType == typeof(long) && value is int or short or byte)
        {
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(int) && value is long or double)
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(short) && value is long or int)
        {
            return Convert.ToInt16(value, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(double) && value is long or int or float)
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(float) && value is long or int or double)
        {
            return Convert.ToSingle(value, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(bool) && value is not bool)
        {
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }

        try
        {
            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            throw new ToolExecutionException($"Value '{value}' cannot be used for parameter of type {targetType.Name}.");
        }
    }

    private static async Task<object?> AwaitResultAsync(object? raw, string toolName, CancellationToken cancellationToken)
    {
        switch (raw)
        {
            case Task<object?> typedTask:
                return await typedTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            case Task task:
                await task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return null;
            case ValueTask<object?> typedValueTask:
                return await typedValueTask.AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
            case ValueTask valueTask:
                await valueTask.AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
                return null;
            default:
                return raw;
        }
    }

    private static Exception Unwrap(Exception exception, string toolName)
        => exception is ToolExecutionException or OperationCanceledException
            ? exception
            : new ToolExecutionException($"Tool '{toolName}' failed: {exception.Message}", exception);

    /// <summary>Coerces a JSON argument element into the CLR value expected by the tool parameter.</summary>
    public static object? Coerce(ToolParameter parameter, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        switch (parameter.Kind)
        {
            case ToolParameterKind.String:
                return element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString(),
                    JsonValueKind.Number => element.ToString(),
                    JsonValueKind.True or JsonValueKind.False => element.ToString(),
                    _ => throw new ToolExecutionException($"Parameter '{parameter.Name}' expects a string."),
                };

            case ToolParameterKind.Integer:
                if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var integer))
                {
                    return integer;
                }

                if (element.ValueKind == JsonValueKind.String
                    && long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out integer))
                {
                    return integer;
                }

                throw new ToolExecutionException($"Parameter '{parameter.Name}' expects an integer.");

            case ToolParameterKind.Number:
                if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var number))
                {
                    return number;
                }

                if (element.ValueKind == JsonValueKind.String
                    && double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                {
                    return number;
                }

                throw new ToolExecutionException($"Parameter '{parameter.Name}' expects a number.");

            case ToolParameterKind.Boolean:
                if (element.ValueKind == JsonValueKind.True)
                {
                    return true;
                }

                if (element.ValueKind == JsonValueKind.False)
                {
                    return false;
                }

                if (element.ValueKind == JsonValueKind.String)
                {
                    return element.GetString()?.Trim().ToLowerInvariant() switch
                    {
                        "true" or "1" or "yes" => true,
                        "false" or "0" or "no" => false,
                        _ => throw new ToolExecutionException($"Parameter '{parameter.Name}' expects a boolean."),
                    };
                }

                throw new ToolExecutionException($"Parameter '{parameter.Name}' expects a boolean.");

            case ToolParameterKind.StringArray:
                if (element.ValueKind == JsonValueKind.Array)
                {
                    return element.EnumerateArray()
                        .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                        .ToArray();
                }

                if (element.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                {
                    return new[] { element.ToString() };
                }

                throw new ToolExecutionException($"Parameter '{parameter.Name}' expects an array of strings.");

            default:
                throw new ToolExecutionException($"Parameter '{parameter.Name}' has unsupported type '{parameter.Kind}'.");
        }
    }
}
