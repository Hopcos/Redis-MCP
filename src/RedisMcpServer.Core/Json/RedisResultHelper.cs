using System.Globalization;
using StackExchange.Redis;

namespace RedisMcpServer.Core.Json;

/// <summary>
/// Converts raw <see cref="RedisResult"/> trees (from <c>ExecuteAsync</c> style commands)
/// into JSON-friendly CLR values suitable for serialization.
/// </summary>
public static class RedisResultHelper
{
    /// <summary>Recursively converts a RedisResult into object / string / long / double / null / arrays / dictionaries.</summary>
    public static object? ToObject(RedisResult result)
    {
        if (result.IsNull)
        {
            return null;
        }

        // Length is -1 for any scalar and >= 0 for arrays (0 = empty array).
        if (result.Length >= 0)
        {
            var items = AsArray(result);
            var converted = new object?[items.Length];
            for (var i = 0; i < items.Length; i++)
            {
                converted[i] = ToObject(items[i]);
            }

            return converted;
        }

        var text = result.ToString();
        if (text is null)
        {
            return null;
        }

        // Redis returns integer replies (e.g. DBSIZE, TTL, EXPIRETIME) as plain integers;
        // ToString gives their decimal representation, so numeric parsing recovers the type.
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
        {
            return longValue;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
        {
            return doubleValue;
        }

        return text;
    }

    /// <summary>Converts a flat array [k1, v1, k2, v2, ...] (e.g. CONFIG GET) into a dictionary.</summary>
    public static Dictionary<string, string> ToFlatDictionary(RedisResult result)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (result is { IsNull: false } && result.Length >= 0)
        {
            var items = AsArray(result);
            for (var i = 0; i + 1 < items.Length; i += 2)
            {
                dict[items[i].ToString() ?? string.Empty] = items[i + 1].ToString() ?? string.Empty;
            }
        }

        return dict;
    }

    /// <summary>Casts a RedisResult array reply to <see cref="RedisResult"/>[] safely (SE.Redis's explicit
    /// operator is annotated possibly-null, which trips nullable analysis when its element count says array).</summary>
    public static RedisResult[] AsArray(RedisResult result)
        => (RedisResult[]?)result ?? Array.Empty<RedisResult>();
}
