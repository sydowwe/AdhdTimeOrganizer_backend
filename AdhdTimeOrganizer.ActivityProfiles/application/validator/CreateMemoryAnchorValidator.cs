using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.validator;

public class CreateMemoryAnchorValidator : Validator<MemoryAnchorRequest>
{
    public CreateMemoryAnchorValidator(IServiceScopeFactory scopeFactory, IHttpContextAccessor http)
    {
        RuleFor(x => x.AnchorMonth).InclusiveBetween(1, 12);
        RuleFor(x => x.AnchorYear).InclusiveBetween(1900, 2200);
        RuleFor(x => x.Rating).InclusiveBetween(1, 10);
        RuleFor(x => x.HighlightNote).NotEmpty().MaximumLength(1000);

        RuleFor(x => x).CustomAsync(async (req, ctx, ct) =>
        {
            var userId = http.HttpContext!.User.GetId();
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DbContext>();

            var info = await db.Set<Activity>()
                .Where(a => a.Id == req.ActivityId && a.UserId == userId)
                // Subqueries rather than a.BacklogProfile / a.BucketListProfile / a.MemoryAnchors:
                // Activity is in Core and no longer names this slice's types. Still one round-trip,
                // and still distinguishes "activity not found/not yours" (info is null) from the two
                // business failures below.
                .Select(a => new
                {
                    HasBacklog = db.Set<ActivityBacklogProfile>().Any(p => p.ActivityId == a.Id),
                    HasBucketList = db.Set<ActivityBucketListProfile>().Any(p => p.ActivityId == a.Id),
                    ExistingCount = db.Set<MemoryAnchor>().Count(m =>
                        m.ActivityId == a.Id &&
                        m.AnchorMonth == req.AnchorMonth &&
                        m.AnchorYear == req.AnchorYear)
                })
                .FirstOrDefaultAsync(ct);

            if (info is null)
            {
                ctx.AddFailure(nameof(req.ActivityId), "Activity not found.");
                return;
            }

            if (!info.HasBacklog && !info.HasBucketList)
                ctx.AddFailure(nameof(req.ActivityId),
                    "Activity must have a Backlog or BucketList profile to be anchored.");
            if (info.ExistingCount >= 2)
                ctx.AddFailure(nameof(req.AnchorMonth),
                    "An activity can have at most 2 memory anchors per month.");
        });
    }
}