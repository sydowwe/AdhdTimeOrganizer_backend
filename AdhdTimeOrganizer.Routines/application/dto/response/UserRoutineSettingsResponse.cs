using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.Routines.application.dto.response.todoList;

/// <summary>
/// The routine-domain per-user settings. A user who has never touched any of them has no row, and gets this
/// with every field at its default rather than a 404.
/// </summary>
public record UserRoutineSettingsResponse
{
    /// <summary>
    /// Week-start of the last dismissed weekly routine review, or <c>null</c> if never dismissed. Serialized
    /// as a plain <c>yyyy-MM-dd</c> date; comparing it against "the week I am looking at" is the client's job.
    /// </summary>
    public DateOnly? RoutineReviewDismissedForWeekStart { get; init; }

    public static UserRoutineSettingsResponse FromEntity(UserRoutineSettings? entity) =>
        new()
        {
            RoutineReviewDismissedForWeekStart = entity?.RoutineReviewDismissedForWeekStart
        };
}
