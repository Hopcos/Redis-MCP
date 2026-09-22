namespace RedisMcpServer.Core.Tools;

/// <summary>
/// JSON-schema-compatible parameter kinds supported by tool definitions.
/// </summary>
public enum ToolParameterKind
{
    /// <summary>JSON string (Redis keys, members, patterns, ids...).</summary>
    String,

    /// <summary>JSON integer (64-bit).</summary>
    Integer,

    /// <summary>JSON number (double).</summary>
    Number,

    /// <summary>JSON boolean.</summary>
    Boolean,

    /// <summary>JSON array of strings.</summary>
    StringArray,
}

/// <summary>Maps a CLR parameter type to the closest supported kind.</summary>
public static class ToolParameterKindExtensions
{
    public static ToolParameterKind FromType(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t == typeof(string)) return ToolParameterKind.String;
        if (t == typeof(long) || t == typeof(int) || t == typeof(short) || t == typeof(byte) || t == typeof(ulong) || t == typeof(uint)) return ToolParameterKind.Integer;
        if (t == typeof(double) || t == typeof(float) || t == typeof(decimal)) return ToolParameterKind.Number;
        if (t == typeof(bool)) return ToolParameterKind.Boolean;
        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(t)) return ToolParameterKind.StringArray;
        return ToolParameterKind.String;
    }
}
