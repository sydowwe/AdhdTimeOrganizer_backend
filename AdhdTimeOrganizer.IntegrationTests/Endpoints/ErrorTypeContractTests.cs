using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.error;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.domain.result;
using Sydowwe.Framework.infrastructure.persistence;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// B18 — the <c>errorType</c> field on the error body, and the only thing that proves it is there: an
/// actual response.
/// </summary>
/// <remarks>
/// <para>
/// Four different situations answer 409 (<see cref="ResultErrorType.DbConcurrencyError"/>,
/// <see cref="ResultErrorType.DbUniqueViolationError"/>, <see cref="ResultErrorType.DbForeignKeyError"/>
/// and a business <see cref="ResultErrorType.Conflict"/>) and three answer 403. The status alone cannot
/// tell "refresh and retry" from "pick another name" from "remove the dependents first", so the client
/// used to word all 409s as a concurrency conflict or re-derive the distinction per call site.
/// </para>
/// <para>
/// <b>This is a silent mechanism, which is why these assert on the body.</b> The type travels on
/// <c>HttpContext.Items</c> and is only rendered by the <c>Errors.ResponseBuilder</c> the host installs
/// in <c>Program.cs</c>. Drop that one line and every endpoint still stamps its type, every status stays
/// right, nothing logs and nothing throws — the field just stops appearing. A route- or status-level
/// test would stay green through that.
/// </para>
/// <para>
/// The statuses asserted here are the ones that were already being sent; B18 changed none of them, and
/// <c>RoutineTimePeriodUniqueIndexTests</c> keeps pinning the 409s independently.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class ErrorTypeContractTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static long UserId => FakeLoggedUserService.TestUserId;

    private static async Task<string?> ReadErrorTypeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        using var json = JsonDocument.Parse(body);
        return json.RootElement.TryGetProperty("errorType", out var errorType) ? errorType.GetString() : null;
    }

    private static object NewPeriod(string text, int lengthInDays) => new
    {
        Text = text,
        Color = "#abcdef",
        LengthInDays = lengthInDays,
        StreakThreshold = 90,
        StreakGraceDays = 0,
        ResetAnchorDay = 1,
        HistoryDepth = 16
    };

    [Fact(DisplayName = "A unique violation 409 says DbUniqueViolationError")]
    public async Task UniqueViolation_CarriesItsOwnErrorType()
    {
        await CreateClient().PostAsJsonAsync("api/routine-time-period", NewPeriod("Duplicated", 3), JsonOpts, CancellationToken);

        var response = await CreateClient().PostAsJsonAsync("api/routine-time-period", NewPeriod("Duplicated", 9), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorTypeAsync(response)).Should().Be(nameof(ResultErrorType.DbUniqueViolationError),
            "the client words a name collision as 'pick another value', not as 'someone else changed this row'");
    }

    /// <summary>
    /// The pre-emptive FK refusal in <c>DeleteActivityRoleEndpoint</c>: the role still has activities, and
    /// deleting it would cascade through every activity FK in the solution.
    /// </summary>
    [Fact(DisplayName = "A role that still has activities 409s with DbForeignKeyError")]
    public async Task ForeignKeyRefusal_CarriesItsOwnErrorType()
    {
        long roleId;
        await using (var db = CreateDbContext())
        {
            var role = new ActivityRole { UserId = UserId, Name = $"Occupied {Guid.NewGuid():N}", Color = "#112233" };
            db.Set<ActivityRole>().Add(role);
            await db.SaveChangesAsync(CancellationToken);

            db.Set<Activity>().Add(new Activity { UserId = UserId, Name = "Dependent", RoleId = role.Id });
            await db.SaveChangesAsync(CancellationToken);
            roleId = role.Id;
        }

        var response = await CreateClient().DeleteAsync($"api/activity-role/{roleId}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorTypeAsync(response)).Should().Be(nameof(ResultErrorType.DbForeignKeyError),
            "the user can clear this one by moving the activities — the client says so, which needs the type");
    }

    /// <summary>
    /// Same endpoint, same 409, different situation: a system role can never be deleted, so the client must
    /// not offer "remove the dependents first". This is the pair that makes the field load-bearing.
    /// </summary>
    [Fact(DisplayName = "A system role 409s with Conflict, distinct from the FK refusal on the same endpoint")]
    public async Task BusinessConflict_CarriesItsOwnErrorType()
    {
        long roleId;
        await using (var db = CreateDbContext())
        {
            var existing = await db.Set<ActivityRole>()
                .FirstOrDefaultAsync(r => r.UserId == UserId && r.SystemKey != null, CancellationToken);

            if (existing is null)
            {
                existing = new ActivityRole
                {
                    UserId = UserId,
                    Name = $"System {Guid.NewGuid():N}",
                    Color = "#112233",
                    SystemKey = SystemActivityRole.RoutineTask
                };
                db.Set<ActivityRole>().Add(existing);
                await db.SaveChangesAsync(CancellationToken);
            }

            roleId = existing.Id;
        }

        var response = await CreateClient().DeleteAsync($"api/activity-role/{roleId}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorTypeAsync(response)).Should().Be(nameof(ResultErrorType.Conflict),
            "nothing the user can do clears a system role, so it must not read as the FK refusal above");
    }

    /// <summary>
    /// FluentValidation 400s stay exactly as they are: their <c>errors</c> map already names the offending
    /// fields, and the client's per-field rendering must not start branching on a type that isn't there.
    /// </summary>
    [Fact(DisplayName = "A validation 400 carries no errorType at all, only its errors map")]
    public async Task ValidationFailure_OmitsErrorType()
    {
        // StreakThreshold is InclusiveBetween(1, 100) in RoutineTimePeriodValidator, so 0 fails the
        // validator and never reaches the database.
        var response = await CreateClient().PostAsJsonAsync("api/routine-time-period", new
        {
            Text = "Invalid",
            Color = "#abcdef",
            LengthInDays = 3,
            StreakThreshold = 0,
            StreakGraceDays = 0,
            ResetAnchorDay = 1,
            HistoryDepth = 16
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        using var json = JsonDocument.Parse(body);
        json.RootElement.TryGetProperty("errorType", out _).Should().BeFalse(
            "absent, not null — the field is only written when the server actually knows the type");
        json.RootElement.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.EnumerateObject().Should().NotBeEmpty("the 400 body's shape is unchanged by B18");
    }
}

/// <summary>
/// The concurrency arm of the same contract, pinned off the HTTP path because no request DTO implements
/// <c>IUpdateRequestWithRowVersion</c> yet, so a <c>DbUpdateConcurrencyException</c> is currently
/// unreachable through an endpoint. When one does, this should become a request-level test like its
/// siblings above.
/// </summary>
public class ErrorTypeMappingTests
{
    [Fact(DisplayName = "A concurrency conflict is still 409 and names itself DbConcurrencyError")]
    public void ConcurrencyConflict_MapsTo409_AndNamesItself()
    {
        var result = DbUtils.HandleException(new DbUpdateConcurrencyException(), "Update");

        result.ErrorType.Should().Be(ResultErrorType.DbConcurrencyError);
        EndpointHelper.ToStatusCode(result.ErrorType).Should().Be(409, "B18 adds a field, it does not move a status");

        var response = new TypedErrorResponse([], 409, result.ErrorType);

        response.ErrorType.Should().Be(nameof(ResultErrorType.DbConcurrencyError),
            "the wire value is the enum member name — the client branches on it and must never word-match the message");
    }
}
