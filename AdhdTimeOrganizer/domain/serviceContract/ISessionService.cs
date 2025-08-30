namespace AdhdTimeOrganizer.domain.serviceContract;

public interface ISessionService
{
    void SetObject<T>(string key, T value);
    T? GetObject<T>(string key);
    bool TryGetObject<T>(string key, out T value);
    void Remove(string key);
}