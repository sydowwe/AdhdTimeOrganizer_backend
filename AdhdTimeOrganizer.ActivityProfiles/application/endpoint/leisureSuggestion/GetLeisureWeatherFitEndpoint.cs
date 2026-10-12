using AdhdTimeOrganizer.ActivityProfiles.application.dto.response;
using AdhdTimeOrganizer.ActivityProfiles.domain.model;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.service;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.leisureSuggestion;

/// <summary>
/// Today's weather, resolved into the caller's own weather-dependency rows: "given where you say you are and
/// what the sky is doing, these of your rows fit".
///
/// <para><b>Why the answer is a list of ids.</b> <c>ActivityWeatherDependency</c> is a per-user, user-editable
/// lookup. The client knows four conditions and has locale strings for them, but nothing on the wire ties a
/// specific row to one — matching on <c>Text</c> would break on the first rename and never work in the second
/// locale. Resolving the set server-side, where the row's <see cref="ActivityWeatherDependency.Code"/> lives,
/// leaves the client with an id comparison that cannot drift.</para>
///
/// <para><b>Every failure is an empty set, deliberately.</b> No location set, a place that does not geocode, a
/// provider outage — all answer 200 with nothing matching. The client reads an empty set and a failed call
/// identically ("no weather opinion"): no badge renders. A 500 here would buy the caller no information it could
/// act on and would put an error in front of a user who merely never filled in a setting.</para>
///
/// <para><b>Since F2-04 this only paints the badge.</b> The ranking reads the same signal inside the draw
/// (<see cref="ILeisureWeatherSignal"/>), so the two cannot disagree about today's weather. This stays its own GET
/// so the badge on the backlog table, which has no draw, can still ask.</para>
/// </summary>
public class GetLeisureWeatherFitEndpoint(DbContext dbContext, ILeisureWeatherSignal weatherSignal)
    : EndpointWithoutRequest<LeisureWeatherFitResponse>
{
    /// <summary>
    /// A fresh instance per call, not a shared static one: this DTO leaves the endpoint on the empty path, and a
    /// single instance handed to every caller is one <c>PostProcess</c> hook away from one request mutating
    /// another's response.
    /// </summary>
    private static LeisureWeatherFitResponse NoSignal() => new() { MatchingWeatherDependencyIds = [] };

    public override void Configure()
    {
        Get("/leisure-weather-fit");
        Roles(this.GetUserRole());
        Summary(s =>
        {
            s.Summary = "Which weather dependencies today's weather fits";
            s.Description = "Resolves the user's weather location into today's conditions and returns the ids of "
                            + "their own activity-weather-dependency rows that fit. An empty list means no signal "
                            + "is available — never an error.";
            s.Response<LeisureWeatherFitResponse>(200, "The matching ids — possibly empty");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();

        var matchingCodes = await weatherSignal.TodayMatchingCodesAsync(userId, maxWait: null, ct);
        if (matchingCodes is null)
        {
            await Send.OkAsync(NoSignal(), ct);
            return;
        }

        var rows = await dbContext.Set<ActivityWeatherDependency>()
            .Where(d => d.UserId == userId)
            .Select(d => new { d.Id, d.Code, d.Text })
            .ToListAsync(ct);

        // A row with no stored code gets one guessed from its label rather than being silently dropped: rows the
        // user created themselves never carry one, and they are exactly the rows a keen user leans on.
        var matchingIds = rows
            .Where(row => WeatherDependencyCodes.Effective(row.Code, row.Text) is { } code && matchingCodes.Contains(code))
            .Select(row => row.Id)
            .ToList();

        await Send.OkAsync(new LeisureWeatherFitResponse { MatchingWeatherDependencyIds = matchingIds }, ct);
    }
}
