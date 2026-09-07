using System.Linq.Expressions;
using AdhdTimeOrganizer.Routines.application.dto.request.todoList;
using AdhdTimeOrganizer.Routines.application.validator;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using FastEndpoints;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineSettings;

/// <summary>
/// Upserts the caller's routine settings — this is where the weekly-review dismissal is written, and the row
/// is created here rather than on read.
/// <para>
/// The upsert shape is <see cref="BaseUserSettingsUpsertEndpoint{TEntity,TRequest}"/>'s, shared with the three
/// module settings endpoints that had the same hand-written body. Both write races are handled there: a lost
/// first-write insert recovers onto the winner's row, and a lost update — two dismissal clicks in flight
/// against a row that already exists, which is the commoner one — is a 409 rather than the 500 it used to be.
/// </para>
/// </summary>
public class UpdateRoutineSettingsEndpoint(DbContext dbContext)
    : BaseUserSettingsUpsertEndpoint<UserRoutineSettings, UserRoutineSettingsRequest>(dbContext)
{
    protected override string Route => "/routine/settings";

    public override void Configure()
    {
        base.Configure();
        Validator<UserRoutineSettingsValidator>();
        Summary(s =>
        {
            s.Summary = "Update routine settings for the current user";
            s.Response(204, "Success");
            s.Response(400, "Bad request");
        });
    }

    protected override Expression<Func<UserRoutineSettings, bool>> MatchRow(long userId, UserRoutineSettingsRequest req) =>
        s => s.UserId == userId;

    // UserId explicitly: this is the only insert path, and it must not depend on the ambient user.
    protected override UserRoutineSettings NewRow(long userId, UserRoutineSettingsRequest req) => new()
    {
        UserId = userId,
        RoutineReviewDismissedForWeekStart = WeekStart(req)
    };

    protected override void Apply(UserRoutineSettings row, UserRoutineSettingsRequest req) =>
        row.RoutineReviewDismissedForWeekStart = WeekStart(req);

    /// <summary>The validator already rejected an unparseable value, so the parse cannot fail here.</summary>
    private static DateOnly? WeekStart(UserRoutineSettingsRequest req)
    {
        req.TryGetRoutineReviewDismissedForWeekStart(out var weekStart);
        return weekStart;
    }
}
