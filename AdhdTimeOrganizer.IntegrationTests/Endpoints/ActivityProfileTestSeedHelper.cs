using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.@base;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The one set of seed helpers for the three Activity*Profile suites. Promoted out of
/// <see cref="ActivityProfileGridTests"/> so a second suite does not become a fourth copy of them.
///
/// <para>Two of these carry a rule that a hand-rolled copy silently loses. A second user MUST be created
/// through <see cref="UserManager{TUser}"/> — the per-user default seeders fire on sign-up, and a
/// hand-inserted User row has no lookups, which makes a cross-user test pass for the wrong reason. And a
/// <see cref="MemoryAnchor"/> has two check constraints (month 1-12, rating 1-10) plus a
/// (ActivityId, AnchorYear, AnchorMonth) unique index, so an ad-hoc row usually trips one for reasons that
/// have nothing to do with the test.</para>
/// </summary>
public static class ActivityProfileTestSeedHelper
{
    public const string Password = "Test@1234!";

    public static async Task<long> CreateSecondUserAsync(IPostgresFixture fixture, string email)
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

    public static async Task<long> SeedRoleAsync(DbContext db, long userId, string name)
    {
        var role = new ActivityRole { UserId = userId, Name = name, Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return role.Id;
    }

    public static async Task<long> SeedCategoryAsync(DbContext db, long userId, string name)
    {
        var category = new ActivityCategory { UserId = userId, Name = name, Color = "#445566" };
        db.Set<ActivityCategory>().Add(category);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return category.Id;
    }

    /// <summary>
    /// <paramref name="roleName"/> and <paramref name="categoryName"/> default to null, which means "derive
    /// a role from the activity name and file it under no category" — the shape most suites want. Pass them
    /// when the test orders by <c>activity.role.name</c> or <c>activity.category.name</c>, where the
    /// classification has to vary independently of the activity name for the assertion to mean anything.
    /// </summary>
    public static async Task<long> SeedActivityAsync(DbContext db, long userId, string name,
        string? roleName = null, string? categoryName = null)
    {
        var roleId = await SeedRoleAsync(db, userId, roleName ?? $"{name} Role");
        var categoryId = categoryName is null ? (long?)null : await SeedCategoryAsync(db, userId, categoryName);

        var activity = new Activity { UserId = userId, Name = name, RoleId = roleId, CategoryId = categoryId };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return activity.Id;
    }

    public static async Task<long> SeedLookupAsync<TLookup>(DbContext db, long userId, string text)
        where TLookup : BaseLookupWithUser, new()
    {
        var lookup = new TLookup { UserId = userId, Text = text };
        db.Set<TLookup>().Add(lookup);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return lookup.Id;
    }

    public static async Task<(long activityId, long profileId)> SeedBacklogProfileAsync(DbContext db, long userId,
        string name = "Backlog Activity", bool isRepeatable = false, string? roleName = null, string? categoryName = null)
    {
        var activityId = await SeedActivityAsync(db, userId, name, roleName, categoryName);
        var locationTypeId = await SeedLookupAsync<ActivityLocationType>(db, userId, $"{name} Home");
        var weatherId = await SeedLookupAsync<ActivityWeatherDependency>(db, userId, $"{name} Indoor");
        var costTierId = await SeedLookupAsync<ActivityExpectedCostTier>(db, userId, $"{name} Free");

        var profile = new ActivityBacklogProfile
        {
            ActivityId = activityId,
            LocationTypeId = locationTypeId,
            WeatherDependencyId = weatherId,
            ExpectedCostTierId = costTierId,
            EnergyLevel = EnergyLevel.Low,
            MinParticipants = 1,
            DurationMinutes = 30,
            IsRepeatable = isRepeatable
        };
        db.Set<ActivityBacklogProfile>().Add(profile);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (activityId, profile.Id);
    }

    public static async Task<(long activityId, long profileId)> SeedBucketListProfileAsync(DbContext db, long userId,
        string name = "Bucket Activity")
    {
        var activityId = await SeedActivityAsync(db, userId, name);
        var experienceTypeId = await SeedLookupAsync<ActivityExperienceType>(db, userId, $"{name} Adventure");

        var profile = new ActivityBucketListProfile
        {
            ActivityId = activityId,
            ExperienceTypeId = experienceTypeId,
            ComfortZoneStep = 5,
            InspirationSource = "A friend's story"
        };
        db.Set<ActivityBucketListProfile>().Add(profile);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (activityId, profile.Id);
    }

    public static async Task<(long activityId, long profileId)> SeedProjectProfileAsync(DbContext db, long userId,
        string name = "Project Activity")
    {
        var activityId = await SeedActivityAsync(db, userId, name);

        var profile = new ActivityProjectProfile
        {
            ActivityId = activityId,
            DifficultyLevel = DifficultyLevel.Beginner,
            ProjectArea = "Woodworking",
            EstimatedHours = 4,
            ReadinessStatus = ReadinessStatus.Planning
        };
        db.Set<ActivityProjectProfile>().Add(profile);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (activityId, profile.Id);
    }

    public static async Task<long> SeedMemoryAnchorAsync(DbContext db, long userId, long activityId, int year, int month)
    {
        var anchor = new MemoryAnchor
        {
            UserId = userId,
            ActivityId = activityId,
            AnchorYear = year,
            AnchorMonth = month,
            HighlightNote = $"Anchored {year}-{month:00}",
            Rating = 8
        };
        db.Set<MemoryAnchor>().Add(anchor);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return anchor.Id;
    }
}
