using System.Globalization;
using System.Text;
using StackExchange.Redis;

namespace RedisMcpServer.Core.Json;

/// <summary>
/// Helpers converting StackExchange.Redis result primitives into JSON-friendly CLR values
/// (string / long / double / null / arrays / dictionaries), tolerant of binary payloads.
/// </summary>
public static class RedisValueHelper
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Converts a <see cref="RedisValue"/> to a display string. Binary data that is not valid
    /// UTF-8 is surfaced as "base64:&lt;...&gt;" so it stays lossless and readable.
    /// </summary>
    public static string? ToDisplayString(RedisValue value)
    {
        if (value.IsNull || !value.HasValue)
        {
            return null;
        }

        if (value.IsInteger && value.TryParse(out long parsed))
        {
            return parsed.ToString(CultureInfo.InvariantCulture);
        }

        try
        {
            // RedisValue has an explicit conversion to byte[]; the (byte[]) cast is binary-safe.
            byte[]? bytes = value;
            if (bytes is null)
            {
                return value.ToString();
            }

            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            byte[]? bytes = value;
            return "base64:" + (bytes is null ? string.Empty : Convert.ToBase64String(bytes));
        }
    }

    /// <summary>Attempts an integer conversion without throwing.</summary>
    public static long? TryGetInt64(RedisValue value)
        => !value.IsNull && value.TryParse(out long parsed) ? parsed : null;

    /// <summary>Attempts a double conversion without throwing.</summary>
    public static double? TryGetDouble(RedisValue value)
    {
        if (!value.HasValue || value.IsNull)
        {
            return null;
        }

        if (value.TryParse(out double parsed))
        {
            return parsed;
        }

        var text = value.ToString();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>Converts a collection of RedisValues into display strings.</summary>
    public static string?[] ToDisplayStrings(RedisValue[] values)
        => values.Select(ToDisplayString).ToArray();
}
