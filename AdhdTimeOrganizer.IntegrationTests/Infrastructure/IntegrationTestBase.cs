using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QRCoder;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Infrastructure;

[Collection("Integration")]
public abstract class IntegrationTestBase(TestWebApplicationFactory factory) : IAsyncLifetime
{
    protected readonly TestWebApplicationFactory factory = factory;
    protected HttpClient client = null!;
    protected HttpClient anonClient = null!;
    protected readonly Uri baseUrl = new("https://localhost/api/");

    protected const string TestEmail = "test@integration.com";
    private const string TestPassword = "Test@1234!";

    public async Task InitializeAsync()
    {
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
            BaseAddress = baseUrl
        });

        anonClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = baseUrl
        });
        await SeedRolesAndUser();
        await LoginAsync();
    }

    private async Task SeedRolesAndUser()
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var roleManager = sp.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<UserRole>>();
        var userManager = sp.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();

        foreach (var (name, level) in new[] { ("User", 1), ("Admin", 3) })
        {
            if (await roleManager.FindByNameAsync(name) == null)
            {
                await roleManager.CreateAsync(new UserRole
                {
                    Name = name,
                    Description = name,
                    IsDefault = name == "User",
                    RoleLevel = level,
                    IsAssignable = true
                });
            }
        }

        if (await userManager.FindByEmailAsync(TestEmail) != null) return;

        var user = new User
        {
            Email = TestEmail,
            EmailConfirmed = true,
            CurrentLocale = AvailableLocales.En,
            Timezone = TimeZoneInfo.Utc
        };
        await userManager.CreateAsync(user, TestPassword);
        await userManager.AddToRoleAsync(user, "User");
    }

    protected async Task<long> GetTestUserIdAsync()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();
        var user = await userManager.FindByEmailAsync(TestEmail);
        return user!.Id;
    }

    private async Task LoginAsync()
    {
        var payload = new
        {
            Email = TestEmail,
            Password = TestPassword,
            StayLoggedIn = false,
            RecaptchaToken = "test",
            Timezone = "UTC"
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/login");
        request.Headers.Add("X-Client-Id", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(payload);
        var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
    }

    protected AppDbContext CreateDbContext()
    {
        var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>();
    }

    public virtual Task DisposeAsync() => Task.CompletedTask;
}