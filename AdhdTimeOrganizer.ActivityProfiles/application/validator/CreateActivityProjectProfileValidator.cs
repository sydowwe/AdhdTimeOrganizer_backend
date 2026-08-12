using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.validator;

public class CreateActivityProjectProfileValidator : Validator<ActivityProjectProfileRequest>
{
    public CreateActivityProjectProfileValidator(IServiceScopeFactory scopeFactory, IHttpContextAccessor http)
    {
        RuleFor(x => x.ProjectArea).NotEmpty().MaximumLength(255);
        RuleFor(x => x.EstimatedHours).GreaterThan(0);

        RuleFor(x => x).CustomAsync(async (req, ctx, ct) =>
        {
            var userId = http.HttpContext!.User.GetId();
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DbContext>();

            var info = await db.Set<Activity>()
                .Where(a => a.Id == req.ActivityId && a.UserId == userId)
                // Subquery rather than a.ProjectProfile — see CreateActivityBacklogProfileValidator.
                .Select(a => new
                {
                    HasProjectProfile = db.Set<ActivityProjectProfile>().Any(p => p.ActivityId == a.Id)
                })
                .FirstOrDefaultAsync(ct);

            if (info is null)
            {
                ctx.AddFailure(nameof(req.ActivityId), "Activity not found.");
                return;
            }

            if (info.HasProjectProfile)
                ctx.AddFailure(nameof(req.ActivityId), "Activity already has a project profile.");
        });
    }
}