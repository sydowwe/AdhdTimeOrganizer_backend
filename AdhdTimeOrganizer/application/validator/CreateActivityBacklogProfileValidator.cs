using AdhdTimeOrganizer.application.dto.request.activity.profile;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.validator;

public class CreateActivityBacklogProfileValidator : Validator<ActivityBacklogProfileRequest>
{
    public CreateActivityBacklogProfileValidator(IServiceScopeFactory scopeFactory, IHttpContextAccessor http)
    {
        RuleFor(x => x.MinParticipants).GreaterThanOrEqualTo(1);
        RuleFor(x => x.MaxParticipants).GreaterThanOrEqualTo(x => x.MinParticipants).When(x => x.MaxParticipants.HasValue);
        RuleFor(x => x.DurationMinutes).GreaterThan(0);

        RuleFor(x => x).CustomAsync(async (req, ctx, ct) =>
        {
            var userId = http.HttpContext!.User.GetId();
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var info = await db.Activities
                .Where(a => a.Id == req.ActivityId && a.UserId == userId)
                .Select(a => new { HasBacklogProfile = a.BacklogProfile != null })
                .FirstOrDefaultAsync(ct);

            if (info is null)
            {
                ctx.AddFailure(nameof(req.ActivityId), "Activity not found.");
                return;
            }

            if (info.HasBacklogProfile)
                ctx.AddFailure(nameof(req.ActivityId), "Activity already has a backlog profile.");
        });
    }
}
