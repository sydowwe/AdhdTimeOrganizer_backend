using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.validator;

public class CreateActivityBucketListProfileValidator : Validator<ActivityBucketListProfileRequest>
{
    public CreateActivityBucketListProfileValidator(IServiceScopeFactory scopeFactory, IHttpContextAccessor http)
    {
        // 1–5, the range LeisureDrawRanker.Score and ComfortStepAsEnergy assume: (6 - step) goes
        // negative from 6 up, and the energy mapping saturates above 3. No DB check constraint —
        // rows already stored in 6–10 are clamped by a data pass, not rejected by the schema.
        RuleFor(x => x.ComfortZoneStep).InclusiveBetween(1, 5);
        RuleFor(x => x.InspirationSource).NotEmpty().MaximumLength(500);
        RuleFor(x => x.FinancialGoal).GreaterThan(0).When(x => x.FinancialGoal.HasValue);

        RuleFor(x => x).CustomAsync(async (req, ctx, ct) =>
        {
            var userId = http.HttpContext!.User.GetId();
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DbContext>();

            var info = await db.Set<Activity>()
                .Where(a => a.Id == req.ActivityId && a.UserId == userId)
                // Subquery rather than a.BucketListProfile — see CreateActivityBacklogProfileValidator.
                .Select(a => new
                {
                    HasBucketListProfile = db.Set<ActivityBucketListProfile>().Any(p => p.ActivityId == a.Id),
                    // The lookup id rides the same round-trip. See LookupOwnership for why it has to be
                    // checked at all.
                    OwnsExperienceType = db.Set<ActivityExperienceType>().Any(e => e.Id == req.ExperienceTypeId && e.UserId == userId)
                })
                .FirstOrDefaultAsync(ct);

            if (info is null)
            {
                ctx.AddFailure(nameof(req.ActivityId), "Activity not found.");
                return;
            }

            if (info.HasBucketListProfile)
                ctx.AddFailure(nameof(req.ActivityId), "Activity already has a bucket list profile.");

            LookupOwnership.AddBucketListFailures(ctx, info.OwnsExperienceType);
        });
    }
}