using AdhdTimeOrganizer.Routines.application.dto.request.todoList;
using AdhdTimeOrganizer.Routines.application.validator;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using FastEndpoints;
using Npgsql;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineSettings;

/// <summary>
/// Upserts the caller's routine settings — this is where the weekly-review dismissal is written, and the row
/// is created here rather than on read.
/// </summary>
public class UpdateRoutineSettingsEndpoint(DbContext dbContext)
    : Endpoint<UserRoutineSettingsRequest>
{
    public override void Configure()
    {
        Put("/routine/settings");

        Validator<UserRoutineSettingsValidator>();
        Summary(s =>
        {
            s.Summary = "Update routine settings for the current user";
            s.Response(204, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(UserRoutineSettingsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        // The validator already rejected an unparseable value; this cannot fail here.
        req.TryGetRoutineReviewDismissedForWeekStart(out var weekStart);

        var settings = await dbContext.Set<UserRoutineSettings>().FirstOrDefaultAsync(s => s.UserId == userId, ct);

        if (settings == null)
        {
            // UserId explicitly: this is the only insert path, and it must not depend on the ambient user.
            settings = new UserRoutineSettings { UserId = userId, RoutineReviewDismissedForWeekStart = weekStart };
            await dbContext.Set<UserRoutineSettings>().AddAsync(settings, ct);

            try
            {
                await dbContext.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Two concurrent first writes both found no row and both inserted against the unique user_id
                // index. Detach and update the winning row — same shape as UpdateReminderKindPreferenceEndpoint,
                // which upserts the same per-user settings row one module over.
                dbContext.ChangeTracker.Clear();
                settings = await dbContext.Set<UserRoutineSettings>().FirstAsync(s => s.UserId == userId, ct);
                settings.RoutineReviewDismissedForWeekStart = weekStart;
                await dbContext.SaveChangesAsync(ct);
            }
        }
        else
        {
            settings.RoutineReviewDismissedForWeekStart = weekStart;
            dbContext.Set<UserRoutineSettings>().Update(settings);
            await dbContext.SaveChangesAsync(ct);
        }

        await Send.NoContentAsync(ct);
    }
}
