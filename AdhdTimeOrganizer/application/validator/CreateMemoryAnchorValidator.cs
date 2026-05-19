using AdhdTimeOrganizer.application.dto.request.activity.memoryAnchor;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AdhdTimeOrganizer.application.validator;

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
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var info = await db.Activities
                .Where(a => a.Id == req.ActivityId && a.UserId == userId)
                .Select(a => new
                {
                    HasBacklog = a.BacklogProfile != null,
                    HasBucketList = a.BucketListProfile != null,
                    ExistingCount = a.MemoryAnchors.Count(m =>
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
