using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.validator;

public class UpdateActivityBucketListProfileValidator : Validator<ActivityBucketListProfileRequest>
{
    public UpdateActivityBucketListProfileValidator(IServiceScopeFactory scopeFactory, IHttpContextAccessor http)
    {
        RuleFor(x => x.ComfortZoneStep).InclusiveBetween(1, 10);
        RuleFor(x => x.InspirationSource).NotEmpty().MaximumLength(500);
        RuleFor(x => x.FinancialGoal).GreaterThan(0).When(x => x.FinancialGoal.HasValue);

        // ExperienceTypeId is rewritten on update — see UpdateActivityBacklogProfileValidator for why
        // the caller, not the request's ActivityId, is the root here.
        RuleFor(x => x).CustomAsync(async (req, ctx, ct) =>
        {
            var userId = http.HttpContext!.User.GetId();
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DbContext>();

            var ownsExperienceType = await db.Set<ActivityExperienceType>()
                .AnyAsync(e => e.Id == req.ExperienceTypeId && e.UserId == userId, ct);

            LookupOwnership.AddBucketListFailures(ctx, ownsExperienceType);
        });
    }
}
