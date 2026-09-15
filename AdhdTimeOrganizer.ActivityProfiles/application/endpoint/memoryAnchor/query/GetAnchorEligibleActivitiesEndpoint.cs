using AdhdTimeOrganizer.ActivityProfiles.application.validator;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.memoryAnchor.query;

/// <summary>
/// The activities the caller may anchor, as picker options: <c>GET /memory-anchor/anchor-eligible-activities</c>.
///
/// <para><b>"Eligible" means exactly what <see cref="CreateMemoryAnchorValidator"/> will accept</b> — the caller's
/// own, un-archived activity that carries a backlog profile <i>or</i> a bucket-list profile. The two are separate
/// copies of one rule: if the picker lists something the validator refuses, the form fails on submit with no
/// way to fix it; if the validator accepts something the picker hides, the "I did this" path can create what the
/// form cannot. Change them together.</para>
///
/// <para><b>An activity that already has an anchor stays listed.</b> An anchor is "this was memorable in month
/// X", not a completion flag, and the create ceiling is two per activity per month — a repeatable backlog
/// activity is expected to collect one every time it is worth remembering. The only refusal is that ceiling,
/// and the validator answers it with a 400 on <c>anchorMonth</c>; the picker does not try to predict it.</para>
///
/// <para>Declared as its own route rather than through <c>BaseGetSelectOptionsEndpoint&lt;Activity&gt;</c>
/// because that base derives <c>/activity/all-options</c> from the entity name and Core already owns that
/// path; this one lives under <c>/memory-anchor</c> because it answers a memory-anchor question.</para>
/// </summary>
public class GetAnchorEligibleActivitiesEndpoint(DbContext dbContext) : EndpointWithoutRequest<List<SelectOptionResponse>>
{
    public override void Configure()
    {
        Get("/memory-anchor/anchor-eligible-activities");
        Roles(this.GetDefaultRoles());
        Summary(s =>
        {
            s.Summary = "Activities that can be given a memory anchor";
            s.Description = "The caller's own un-archived activities that carry a backlog or bucket-list profile, "
                            + "as select options ordered by name. Activities that already have an anchor are "
                            + "included; the only create-time refusal is the two-per-month ceiling.";
            s.Response<List<SelectOptionResponse>>(200, "The options — possibly empty");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();

        // Both predicates are hand-written on purpose: the host's global filters cover them, but a slice
        // cannot see the host and may not lean on it (see docs/summary.md, "Gotchas").
        var options = await dbContext.Set<Activity>()
            .AsNoTracking()
            .Where(a => a.UserId == userId && !a.IsArchived)
            .Where(a => dbContext.Set<ActivityBacklogProfile>().Any(p => p.ActivityId == a.Id)
                        || dbContext.Set<ActivityBucketListProfile>().Any(p => p.ActivityId == a.Id))
            .OrderBy(a => a.Name)
            .ThenBy(a => a.Id)
            .Select(a => new SelectOptionResponse { Id = a.Id, Text = a.Name })
            .ToListAsync(ct);

        await Send.OkAsync(options, ct);
    }
}
