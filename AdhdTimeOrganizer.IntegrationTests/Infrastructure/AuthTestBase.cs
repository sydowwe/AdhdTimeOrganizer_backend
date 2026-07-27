using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Sydowwe.Framework.Testing;

namespace AdhdTimeOrganizer.IntegrationTests.Infrastructure;

/// <summary>
/// Base for tests that exercise the real login/refresh/logout pipeline end-to-end (cookies, JWT, Identity
/// lockout, 2FA) rather than the RoleTestAuthHandler bypass every other test uses. Builds clients directly
/// off the fixture's unauthenticated factory so requests hit the real auth layer.
/// </summary>
public abstract class AuthTestBase(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    protected const string TestEmail = FakeLoggedUserService.TestUserEmail;
    protected const string TestPassword = AppDbContextFixture.TestUserPassword;

    private static readonly Uri BaseUrl = new("https://localhost/api/");

    protected HttpClient CreateCookieClient() =>
        fixture.UnauthenticatedFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
            BaseAddress = BaseUrl
        });

    /// <summary>
    /// Client with no cookie container, so the test controls the <c>Cookie</c> header byte for byte.
    /// Needed when the point of the test is a specific cookie combination (e.g. a stale access token
    /// next to a live refresh token) that a real container would never hand you.
    /// </summary>
    protected HttpClient CreateRawClient() =>
        fixture.UnauthenticatedFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false,
            BaseAddress = BaseUrl
        });

    protected static async Task LoginAsync(HttpClient client, string email, string password)
    {
        var payload = new { Email = email, Password = password, StayLoggedIn = false, RecaptchaToken = "test", Timezone = "UTC" };
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/login");
        request.Headers.Add("X-Client-Id", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(payload);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}