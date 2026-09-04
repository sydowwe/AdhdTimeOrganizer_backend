using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using AdhdTimeOrganizer.ActivityProfiles.application.validator;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.application.endpoint.extensions;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.memoryAnchor.command;

public class UpdateMemoryAnchorEndpoint(DbContext dbContext, IReferencedIdOwnershipService ownership)
    : BaseUpdateEndpoint<MemoryAnchor, MemoryAnchorRequest>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    public override void Configure()
    {
        base.Configure();
        Validator<UpdateMemoryAnchorValidator>();
    }

    /// <summary>
    /// <c>UpdateEntity</c> no longer writes <c>ActivityId</c>, so an edit cannot repoint an anchor at all.
    /// The guard stays: a body naming an activity that is not the caller's is refused outright rather than
    /// silently ignored, and — because it answers the same 404 for a foreign id and a non-existent one —
    /// the route is not an existence oracle over another account's id range either.
    /// </summary>
    protected override Task<bool> BeforeMapping(MemoryAnchorRequest req, CancellationToken ct = default) =>
        this.EnsureOwnsActivityAsync(req, ownership, DbContext, ct);

    /// <summary>
    /// The update half of <c>CreateMemoryAnchorValidator</c>'s "at most 2 anchors per activity per month".
    /// With <c>ActivityId</c> immutable the only way left past that ceiling is editing the month or the
    /// year onto a period that is already full, so that is the only case checked — and only when one of
    /// the two actually changed, or a note-only edit of an already-over-quota activity would be refused.
    ///
    /// <para>It lives here rather than in the validator because the rule is about the row's *stored*
    /// activity, and a FastEndpoints validator sees the request only — neither the route id nor the loaded
    /// entity. <c>AfterMapping</c> has both. The ceiling has no DB backing on purpose
    /// (<c>MemoryAnchorConfiguration</c>'s index is non-unique — a "≤ 2" rule is not a unique index), so
    /// application code is the whole of it.</para>
    /// </summary>
    protected override async Task<bool> AfterMapping(MemoryAnchor entity, MemoryAnchorRequest req, CancellationToken ct = default)
    {
        // Against OriginalValue rather than IsModified: original values are captured when the row is
        // tracked and are correct without a DetectChanges pass, which nothing has run at this point.
        var entry = _dbContext.Entry(entity);
        var periodChanged =
            entry.Property(e => e.AnchorMonth).OriginalValue != entity.AnchorMonth ||
            entry.Property(e => e.AnchorYear).OriginalValue != entity.AnchorYear;
        if (!periodChanged)
            return true;

        // Server-side count of the sibling rows only; the edited row is excluded by id, and the global
        // IEntityWithUser filter keeps it to the caller's own anchors.
        var siblings = await _dbContext.Set<MemoryAnchor>()
            .CountAsync(m =>
                m.Id != entity.Id &&
                m.ActivityId == entity.ActivityId &&
                m.AnchorMonth == entity.AnchorMonth &&
                m.AnchorYear == entity.AnchorYear, ct);

        if (siblings < 2)
            return true;

        AddError(r => r.AnchorMonth, "An activity can have at most 2 memory anchors per month.");
        await Send.ErrorsAsync(400, ct);
        return false;
    }
}
