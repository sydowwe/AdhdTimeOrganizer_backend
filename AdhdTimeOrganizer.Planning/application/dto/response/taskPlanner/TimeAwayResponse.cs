using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;

namespace AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;

/// <summary>
/// One stretch of time away, as the list and the get-by-id read it. Carries no count of any kind on purpose: the
/// list is a quiet record of trips and sick spells, never a tally ("12 sick days this year").
/// </summary>
public record TimeAwayResponse : IdResponse, IProjectionResponse<TimeAwayResponse, TimeAway>
{
    public required string Name { get; init; }
    public required DayType Kind { get; init; }
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }
    public long? TemplateId { get; init; }

    /// <summary>The template's current name, so the list can show it without a second request.</summary>
    public string? TemplateName { get; init; }

    public Location? Location { get; init; }
    public string? Notes { get; init; }

    public static IQueryable<TimeAwayResponse> Projection(IQueryable<TimeAway> query) =>
        query.Select(t => new TimeAwayResponse
        {
            Id = t.Id,
            Name = t.Name,
            Kind = t.Kind,
            StartDate = t.StartDate,
            EndDate = t.EndDate,
            TemplateId = t.TemplateId,
            TemplateName = t.Template != null ? t.Template.Name : null,
            Location = t.Location,
            Notes = t.Notes
        });
}
