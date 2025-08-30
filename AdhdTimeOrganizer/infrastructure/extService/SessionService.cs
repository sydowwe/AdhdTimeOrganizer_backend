using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.serviceContract;

namespace AdhdTimeOrganizer.infrastructure.extService;

public class SessionService(IHttpContextAccessor httpContextAccessor) : IScopedService, ISessionService
{
    private ISession Session =>
        httpContextAccessor.HttpContext?.Session ?? throw new Exception("No Active HttpContext");

    public void SetObject<T>(string key, T value)
    {
        Session.SetObject(key, value);
    }

    public T? GetObject<T>(string key)
    {
        return Session.GetObject<T>(key);
    }

    public bool TryGetObject<T>(string key, out T value)
    {
        return Session.TryGetObject(key, out value);
    }

    public void Remove(string key)
    {
        Session.Remove(key);
    }
}