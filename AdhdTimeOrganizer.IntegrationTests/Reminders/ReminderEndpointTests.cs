using System.Net;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Sydowwe.Framework.Testing.baseTests;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Reminders;

/// <summary>
/// The shared auth matrix + 404 paths for the reminder CRUD surface, from the framework's endpoint test
/// bases. Behaviour specific to reminders — registration, cancellation, IDOR — lives in
/// <see cref="ReminderRegistrationTests"/>.
/// </summary>
[Collection("Postgres")]
public class CreateReminderEndpointTests(AppDbContextFixture fixture) : BaseCreateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/reminder";

    protected override Task<object> BuildValidPayloadAsync(DbContext db) => Task.FromResult<object>(new
    {
        Title = "Call the dentist",
        RemindAt = DateTime.UtcNow.AddHours(2),
        LeadOffsetsMinutes = new[] { -10, 0 }
    });
}

[Collection("Postgres")]
public class UpdateReminderEndpointTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/reminder";

    /// <summary>
    /// Reminders carry <c>AppDbContext</c>'s global user query filter, so another user's row is filtered out
    /// of the lookup before the endpoint's ownership hook can run — the answer is "not found", which is also
    /// the answer that leaks least.
    /// </summary>
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override async Task<long> SeedEntityAsync(DbContext db) =>
        (await ReminderSeedHelper.SeedReminderAsync(db, FakeLoggedUserService.TestUserId, DateTime.UtcNow.AddHours(2), ct: TestContext.Current.CancellationToken)).Id;

    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) => Task.FromResult<object>(new
    {
        Title = "Call the dentist (moved)",
        RemindAt = DateTime.UtcNow.AddHours(3),
        LeadOffsetsMinutes = new[] { 0 }
    });

    /// <summary>The update endpoint scopes by owner, so the base's IDOR case applies here.</summary>
    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var ct = TestContext.Current.CancellationToken;
        await ReminderSeedHelper.EnsureOtherUserAsync(db, ct);
        return (await ReminderSeedHelper.SeedReminderAsync(db, ReminderSeedHelper.OtherUserId, DateTime.UtcNow.AddHours(2), ct: ct)).Id;
    }
}

[Collection("Postgres")]
public class DeleteReminderEndpointTests(AppDbContextFixture fixture) : BaseDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/reminder";

    /// <summary>Filtered out of the lookup, same as the update endpoint — see the note there.</summary>
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override async Task<long> SeedEntityAsync(DbContext db) =>
        (await ReminderSeedHelper.SeedReminderAsync(db, FakeLoggedUserService.TestUserId, DateTime.UtcNow.AddHours(2), ct: TestContext.Current.CancellationToken)).Id;

    /// <summary>The delete endpoint scopes by owner, so the base's IDOR case applies here.</summary>
    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var ct = TestContext.Current.CancellationToken;
        await ReminderSeedHelper.EnsureOtherUserAsync(db, ct);
        return (await ReminderSeedHelper.SeedReminderAsync(db, ReminderSeedHelper.OtherUserId, DateTime.UtcNow.AddHours(2), ct: ct)).Id;
    }
}

[Collection("Postgres")]
public class GetByIdReminderEndpointTests(AppDbContextFixture fixture) : BaseGetByIdEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/reminder";

    /// <summary>
    /// Reads are scoped by <c>AppDbContext</c>'s user query filter rather than by an authorization hook, so
    /// another user's reminder is simply not in the set — indistinguishable from a missing row, which is the
    /// stronger answer anyway (a 403 would confirm the id is real).
    /// </summary>
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override async Task<long> SeedEntityAsync(DbContext db) =>
        (await ReminderSeedHelper.SeedReminderAsync(db, FakeLoggedUserService.TestUserId, DateTime.UtcNow.AddHours(2), ct: TestContext.Current.CancellationToken)).Id;

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var ct = TestContext.Current.CancellationToken;
        await ReminderSeedHelper.EnsureOtherUserAsync(db, ct);
        return (await ReminderSeedHelper.SeedReminderAsync(db, ReminderSeedHelper.OtherUserId, DateTime.UtcNow.AddHours(2), ct: ct)).Id;
    }
}