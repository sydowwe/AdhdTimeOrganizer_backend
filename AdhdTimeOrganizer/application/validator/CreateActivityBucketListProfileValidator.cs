using AdhdTimeOrganizer.application.dto.request.activity.profile;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.validator;

public class CreateActivityBucketListProfileValidator : Validator<ActivityBucketListProfileRequest>
{
    public CreateActivityBucketListProfileValidator(IServiceScopeFactory scopeFactory, IHttpContextAccessor http)
    {
        RuleFor(x => x.ComfortZoneStep).InclusiveBetween(1, 10);
        RuleFor(x => x.InspirationSource).NotEmpty().MaximumLength(500);
        RuleFor(x => x.FinancialGoal).GreaterThan(0).When(x => x.FinancialGoal.HasValue);

        RuleFor(x => x).CustomAsync(async (req, ctx, ct) =>
        {
            var userId = http.HttpContext!.User.GetId();
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var info = await db.Activities
                .Where(a => a.Id == req.ActivityId && a.UserId == userId)
                .Select(a => new { HasBucketListProfile = a.BucketListProfile != null })
                .FirstOrDefaultAsync(ct);

            if (info is null)
            {
                ctx.AddFailure(nameof(req.ActivityId), "Activity not found.");
                return;
            }

            if (info.HasBucketListProfile)
                ctx.AddFailure(nameof(req.ActivityId), "Activity already has a bucket list profile.");
        });
    }
}
