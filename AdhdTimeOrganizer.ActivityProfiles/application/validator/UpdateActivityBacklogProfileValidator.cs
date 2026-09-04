using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.validator;

public class UpdateActivityBacklogProfileValidator : Validator<ActivityBacklogProfileRequest>
{
    public UpdateActivityBacklogProfileValidator(IServiceScopeFactory scopeFactory, IHttpContextAccessor http)
    {
        RuleFor(x => x.MinParticipants).GreaterThanOrEqualTo(1);
        RuleFor(x => x.MaxParticipants).GreaterThanOrEqualTo(x => x.MinParticipants).When(x => x.MaxParticipants.HasValue);
        RuleFor(x => x.DurationMinutes).GreaterThan(0);

        // The update rewrites all three lookup FKs (ActivityBacklogProfileRequest.UpdateEntity), so this
        // is as much of a write surface for them as the create is — hence the same check. It cannot reuse
        // the create's shape: UpdateEntity deliberately does not touch ActivityId, so the request's
        // ActivityId is not what the row will point at and must not be used as the query's root. The
        // caller is, which keeps this one round-trip too.
        RuleFor(x => x).CustomAsync(async (req, ctx, ct) =>
        {
            var userId = http.HttpContext!.User.GetId();
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DbContext>();

            var owned = await db.Set<User>()
                .Where(u => u.Id == userId)
                .Select(u => new
                {
                    OwnsLocationType = db.Set<ActivityLocationType>().Any(l => l.Id == req.LocationTypeId && l.UserId == userId),
                    OwnsWeatherDependency = db.Set<ActivityWeatherDependency>().Any(w => w.Id == req.WeatherDependencyId && w.UserId == userId),
                    OwnsExpectedCostTier = db.Set<ActivityExpectedCostTier>().Any(c => c.Id == req.ExpectedCostTierId && c.UserId == userId)
                })
                .FirstOrDefaultAsync(ct);

            LookupOwnership.AddBacklogFailures(ctx,
                owned?.OwnsLocationType ?? false,
                owned?.OwnsWeatherDependency ?? false,
                owned?.OwnsExpectedCostTier ?? false);
        });
    }
}
