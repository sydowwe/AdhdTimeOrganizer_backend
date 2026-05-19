using AdhdTimeOrganizer.application.dto.request.activity.profile;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AdhdTimeOrganizer.application.validator;

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
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var info = await db.Activities
                .Where(a => a.Id == req.ActivityId && a.UserId == userId)
                .Select(a => new { HasProjectProfile = a.ProjectProfile != null })
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
