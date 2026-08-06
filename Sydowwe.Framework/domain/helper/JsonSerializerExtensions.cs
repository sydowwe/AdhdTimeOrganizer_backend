using System.Text.Json;

namespace Sydowwe.Framework.domain.helper;

public static class JsonHelper
{
    private static JsonSerializerOptions DefaultOptions(bool writeIndented) =>
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = writeIndented
        };

    public static string Serialize<T>(T value, bool writeIndented = false) => JsonSerializer.Serialize(value, DefaultOptions(writeIndented));


    public static T? Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, DefaultOptions(false));
}