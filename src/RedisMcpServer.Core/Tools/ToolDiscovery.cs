using System.ComponentModel;
using System.Reflection;

namespace RedisMcpServer.Core.Tools;

/// <summary>A discovered candidate: one [McpTool]-attributed method on an [McpToolGroup]-attributed class.</summary>
public sealed record ToolMethodInfo(Type ClassType, MethodInfo Method, McpToolAttribute Attribute, string Group);

/// <summary>
/// Static discovery of tool classes/methods from an assembly, and construction of
/// <see cref="ITool"/> instances bound to DI-resolved class instances.
/// </summary>
public static class ToolDiscovery
{
    /// <summary>Scans one assembly for classes marked [McpToolGroup] and methods marked [McpTool].</summary>
    public static IReadOnlyList<ToolMethodInfo> Scan(Assembly assembly)
    {
        var result = new List<ToolMethodInfo>();

        foreach (var type in assembly.GetTypes())
        {
            if (!type.IsClass || type.IsAbstract || !type.IsPublic)
            {
                continue;
            }

            var groupAttribute = type.GetCustomAttribute<McpToolGroupAttribute>();
            if (groupAttribute is null)
            {
                continue;
            }

            var group = string.IsNullOrWhiteSpace(groupAttribute.Name) ? type.Name : groupAttribute.Name!;

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var toolAttribute = method.GetCustomAttribute<McpToolAttribute>();
                if (toolAttribute is null)
                {
                    continue;
                }

                if (method.IsStatic)
                {
                    throw new InvalidOperationException(
                        $"Tool method '{type.Name}.{method.Name}' must be an instance method so it can receive DI-injected services.");
                }

                result.Add(new ToolMethodInfo(type, method, toolAttribute, group));
            }
        }

        return result;
    }

    /// <summary>
    /// Builds the final <see cref="ITool"/> list: resolves one instance per class through
    /// <paramref name="instanceFactory"/> and reflects parameter metadata.
    /// </summary>
    public static IReadOnlyList<ITool> Build(
        IEnumerable<ToolMethodInfo> discovered,
        Func<Type, object> instanceFactory)
    {
        var tools = new List<ITool>();
        foreach (var info in discovered)
        {
            var instance = instanceFactory(info.ClassType);

            var parameters = new List<ToolParameter>();
            foreach (var parameter in info.Method.GetParameters())
            {
                if (parameter.ParameterType == typeof(CancellationToken))
                {
                    continue; // injected at invocation time, not part of the input schema
                }

                parameters.Add(new ToolParameter(
                    Name: parameter.Name ?? "arg",
                    Kind: ToolParameterKindExtensions.FromType(parameter.ParameterType),
                    Description: parameter.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty,
                    IsRequired: !parameter.HasDefaultValue,
                    DefaultValue: parameter.HasDefaultValue ? parameter.DefaultValue : null,
                    AllowedValues: parameter.GetCustomAttribute<ToolParamEnumAttribute>()?.Values));
            }

            tools.Add(new ToolDefinition(
                name: info.Attribute.Name ?? info.Method.Name,
                description: info.Attribute.Description,
                group: info.Group,
                parameters: parameters,
                handlerMethod: info.Method,
                target: instance));
        }

        return tools;
    }
}
