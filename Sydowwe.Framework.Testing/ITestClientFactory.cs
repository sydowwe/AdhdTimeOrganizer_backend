using Microsoft.AspNetCore.Mvc.Testing;

namespace Sydowwe.Framework.Testing;

/// <summary>
/// Portal-agnostic view over a <see cref="WebApplicationFactory{TProgram}"/> — exposes just the
/// client-creation surface the test bases need, so they don't have to know the portal's entry-point type.
/// Implemented by <see cref="TestWebApplicationFactory{TProgram}"/> (both members are satisfied by the
/// inherited <c>WebApplicationFactory</c> methods).
/// </summary>
public interface ITestClientFactory : IAsyncDisposable
{
    HttpClient CreateClient();
    HttpClient CreateClient(WebApplicationFactoryClientOptions options);

    /// <summary>The host's root service provider — for tests that resolve services directly.</summary>
    IServiceProvider Services { get; }
}