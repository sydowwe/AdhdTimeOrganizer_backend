using AdhdTimeOrganizer.ActivityProfiles.application.dto.response;
using AdhdTimeOrganizer.ActivityProfiles.domain.model;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.service;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
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
/// identically ("no weather opinion"): nothing ranks up, no badge renders, nothing is excluded. A 500 here would
/// buy the caller no information it could act on and would put an error in front of a user who merely never
/// filled in a setting.</para>
///
/// <para><b>Nothing here blocks the draw.</b> This is its own GET precisely so the picker's first cards do not
/// wait on a third party; the client fires it alongside the draw and never retries.</para>
/// </summary>
public class GetLeisureWeatherFitEndpoint(
    DbContext dbContext,
    IDailyWeatherProvider weatherProvider,
    ILogger<GetLeisureWeatherFitEndpoint> logger)
    : EndpointWithoutRequest<LeisureWeatherFitResponse>
{
    private static readonly LeisureWeatherFitResponse NoSignal = new() { MatchingWeatherDependencyIds = [] };

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

        // User is not IEntityWithUser and carries no global query filter, so the id predicate is the scoping.
        var location = await dbContext.Set<User>()
            .Where(u => u.Id == userId)
            .Select(u => u.WeatherLocation)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(location))
        {
            await Send.OkAsync(NoSignal, ct);
            return;
        }

        // IDailyWeatherProvider promises never to throw; this catch is what makes "this endpoint is never an
        // error" true of the endpoint rather than merely of the implementation currently registered. The
        // location itself stays out of the log line — it is the user's town.
        DailyWeather? weather;
        try
        {
            weather = await weatherProvider.GetTodayAsync(location, ct);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(exception,
                "The weather provider threw rather than returning no signal; user {UserId} gets no weather opinion today",
                userId);
            weather = null;
        }

        if (weather is null)
        {
            await Send.OkAsync(NoSignal, ct);
            return;
        }

        var matchingCodes = WeatherFitRule.MatchingCodes(weather);

        var rows = await dbContext.Set<ActivityWeatherDependency>()
            .Where(d => d.UserId == userId)
            .Select(d => new { d.Id, d.Code, d.Text })
            .ToListAsync(ct);

        // A row with no stored code gets one guessed from its label rather than being silently dropped: rows the
        // user created themselves never carry one, and they are exactly the rows a keen user leans on.
        var matchingIds = rows
            .Where(row => (row.Code ?? WeatherDependencyCodes.Infer(row.Text)) is { } code && matchingCodes.Contains(code))
            .Select(row => row.Id)
            .ToList();

        await Send.OkAsync(new LeisureWeatherFitResponse { MatchingWeatherDependencyIds = matchingIds }, ct);
    }
}
