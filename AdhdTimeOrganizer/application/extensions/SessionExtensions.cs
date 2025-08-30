using Newtonsoft.Json;

namespace AdhdTimeOrganizer.application.extensions;

public static class SessionExtensions
{
    public static void SetObject<T>(this ISession session, string key, T value)
    {
        var jsonData = JsonConvert.SerializeObject(value);
        session.SetString(key, jsonData);
    }

    public static T? GetObject<T>(this ISession session, string key)
    {
        var jsonData = session.GetString(key);
        return jsonData == null ? default : JsonConvert.DeserializeObject<T>(jsonData);
    }
    public static bool TryGetObject<T>(this ISession session, string key, out T value)
    {
        var tmp = session.GetObject<T>(key);
        if (tmp == null)
        {
            value = default!;
            return false;
        }
        value = tmp;
        return true;
    }
}