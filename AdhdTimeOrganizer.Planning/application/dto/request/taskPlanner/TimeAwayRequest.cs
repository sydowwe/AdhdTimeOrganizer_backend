using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;

/// <summary>
/// Body of <c>POST /time-away</c> and <c>PUT /time-away/{id}</c>. The same shape both ways: a PUT is how a stretch
/// is renamed, resized, moved or re-kinded, and the stamp is re-applied from what it says.
/// </summary>
public record TimeAwayRequest : IMyRequest<TimeAway>
{
    public required string Name { get; init; }

    /// <summary><c>Vacation</c> or <c>SickDay</c>; the other three are a 400.</summary>
    public required DayType Kind { get; init; }

    /// <summary>First day, inclusive.</summary>
    public required DateOnly StartDate { get; init; }

    /// <summary>Last day, inclusive.</summary>
    public required DateOnly EndDate { get; init; }

    /// <summary>Suggested first on the stretch's days; never applied. Must be one of the caller's templates.</summary>
    public long? TemplateId { get; init; }

    public Location? Location { get; init; }

    public string? Notes { get; init; }

    /// <summary>The endpoint sets <c>UserId</c>: it is the one that knows who is asking.</summary>
    public TimeAway ToEntity => new()
    {
        UserId = 0,
        Name = Name,
        Kind = Kind,
        StartDate = StartDate,
        EndDate = EndDate,
        TemplateId = TemplateId,
        Location = Location,
        Notes = Notes
    };

    public void UpdateEntity(TimeAway entity)
    {
        entity.Name = Name;
        entity.Kind = Kind;
        entity.StartDate = StartDate;
        entity.EndDate = EndDate;
        entity.TemplateId = TemplateId;
        entity.Location = Location;
        entity.Notes = Notes;
    }
}
