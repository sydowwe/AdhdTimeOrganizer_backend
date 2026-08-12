using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.validator;

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
            var db = scope.ServiceProvider.GetRequiredService<DbContext>();

            var info = await db.Set<Activity>()
                .Where(a => a.Id == req.ActivityId && a.UserId == userId)
                // Subquery rather than a.BacklogProfile: Activity is in Core and no longer names this
                // slice's types. Still one round-trip, and still distinguishes "activity not
                // found/not yours" (info is null) from "already profiled".
                .Select(a => new
                {
                    HasBacklogProfile = db.Set<ActivityBacklogProfile>().Any(p => p.ActivityId == a.Id)
                })
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