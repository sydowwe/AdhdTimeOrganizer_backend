using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Core.application.dto.response.extendable;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.History.domain.model.@enum;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.domain.valueObject;

namespace AdhdTimeOrganizer.History.application.dto.response.activityHistory;

public record ActivityHistoryResponse : WithActivityResponse, IProjectionResponse<ActivityHistoryResponse, ActivityHistory>
{
    public required DateTime StartTimestamp { get; init; }
    public required IntTime Length { get; init; }
    public required DateTime EndTimestamp { get; init; }

    /// <summary>Null for a row outside a session and for a countdown's single row.</summary>
    public TimerSessionPhase? Phase { get; init; }

    /// <summary>Null for every row written outside a session — including all rows written before sessions existed.</summary>
    public ActivityHistorySessionResponse? Session { get; init; }

    public static IQueryable<ActivityHistoryResponse> Projection(IQueryable<ActivityHistory> query)
    {
        return query.Select(e => new ActivityHistoryResponse
        {
            Id = e.Id,
            Activity = new ActivityResponse
            {
                Id = e.Activity.Id,
                Name = e.Activity.Name,
                Text = e.Activity.Text,
                IsUnavoidable = e.Activity.IsUnavoidable,
                // Left unprojected, this defaulted to false on every row. That was invisible while archived
                // activities' sessions were being dropped from the list altogether; now that they are
                // returned (see ActivityHistoryReadExtensions) it would be a lie the client cannot detect —
                // a retired activity rendered exactly like a live one, in the one list that shows both.
                IsArchived = e.Activity.IsArchived,
                Role = new ActivityRoleResponse { Id = e.Activity.Role.Id, Name = e.Activity.Role.Name, Text = e.Activity.Role.Text, Color = e.Activity.Role.Color, Icon = e.Activity.Role.Icon },
                Category = e.Activity.Category == null
                    ? null
                    : new ActivityCategoryResponse
                        { Id = e.Activity.Category.Id, Name = e.Activity.Category.Name, Text = e.Activity.Category.Text, Color = e.Activity.Category.Color, Icon = e.Activity.Category.Icon }
            },
            StartTimestamp = e.StartTimestamp,
            Length = e.Length,
            EndTimestamp = e.EndTimestamp,
            Phase = e.Phase,
            // An optional navigation, so the session's own user filter can only blank it, never drop the row —
            // and it is always the caller's own anyway, since it was written in the same request as its rows.
            Session = e.Session == null
                ? null
                : new ActivityHistorySessionResponse
                {
                    Id = e.Session.Id,
                    Kind = e.Session.Kind,
                    Outcome = e.Session.Outcome,
                    PlannedFocusPeriods = e.Session.PlannedFocusPeriods,
                    CompletedFocusPeriods = e.Session.CompletedFocusPeriods,
                    PlannedLength = e.Session.PlannedLength
                }
        });
    }
}