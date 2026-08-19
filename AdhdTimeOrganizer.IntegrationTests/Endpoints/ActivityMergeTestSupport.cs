using AdhdTimeOrganizer.Core.domain.model.entity.user;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.Testing;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// Seeding shared by <see cref="ActivityArchivingTests"/> and <see cref="ActivityMergeTests"/>.
/// </summary>
/// <remarks>
/// Both files need a second real user to prove that a cross-user id answers 404 rather than 403, and
/// creating one goes through <c>UserManager</c> (not a bare insert) so the per-user default seeders run
/// and the account is the same shape a real sign-up produces.
/// </remarks>
internal static class ActivityMergeTestSupport
{
    private const string Password = "Test@1234!";

    public static async Task<long> SecondUserAsync(IPostgresFixture fixture, string email)
    {
        using var scope = fixture.UnauthenticatedFactory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User
        {
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            Locale = AvailableLocales.En,
            Timezone = TimeZoneInfo.Utc
        };

        var result = await userManager.CreateAsync(user, Password);
        result.Succeeded.Should().BeTrue();
        return user.Id;
    }
}
