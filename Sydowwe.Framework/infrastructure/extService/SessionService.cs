using Microsoft.AspNetCore.Http;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.serviceContract;

namespace Sydowwe.Framework.infrastructure.extService;

public class SessionService(IHttpContextAccessor httpContextAccessor) : IScopedService, ISessionService
{
    private ISession Session =>
        httpContextAccessor.HttpContext?.Session ?? throw new Exception("No Active HttpContext");

    public void SetObject<T>(string key, T value)
    {
        Session.SetObject(key, value);
    }

    public T? GetObject<T>(string key) => Session.GetObject<T>(key);

    public bool TryGetObject<T>(string key, out T value) => Session.TryGetObject(key, out value);

    public void Remove(string key)
    {
        Session.Remove(key);
    }
}