using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using FastEndpoints;
using FluentValidation.Results;

namespace AdhdTimeOrganizer.Planning.application.endpoint.extensions;

/// <summary>
/// The two rules a stretch of time away's create and update share that a validator cannot see, because both need
/// the database: <b>no overlap</b> with the user's other stretches, and a <c>TemplateId</c> that is the user's own.
/// </summary>
public static class TimeAwayWriteRules
{
    /// <summary>
    /// Writes the response and returns <c>false</c> when the request breaks a rule: a 400 on <c>StartDate</c> for an
    /// overlap, naming the stretch it runs into, and the slice's usual 404 for a template that is not the caller's
    /// (<see cref="PlannerReferencedIdOwnershipExtensions.EnsureOwnsDayTemplateAsync"/>).
    /// <para>
    /// One stretch per day is what lets a day carry a single <c>TimeAwayId</c>. Adjacent stretches are fine — the
    /// range test is inclusive at both ends, so 12–20 Aug and 21–25 Aug do not touch. <paramref name="exceptId"/>
    /// is the stretch being updated, which may of course overlap its own old range.
    /// </para>
    /// <para>
    /// Not race-proof on its own: two requests creating overlapping stretches at the same instant can both pass.
    /// The days are where they collide — both stamps write the same calendar rows, and the second save loses on
    /// <c>row_version</c> (or on <c>(user_id, date)</c> for a day both created) as a 409.
    /// </para>
    /// </summary>
    public static async Task<bool> EnsureTimeAwayWritableAsync(
        this IEndpoint endpoint, DbContext dbContext, long userId, TimeAwayRequest req, long? exceptId, CancellationToken ct)
    {
        var clash = await dbContext.Set<TimeAway>()
            .Where(t => t.UserId == userId
                        && (exceptId == null || t.Id != exceptId)
                        && t.StartDate <= req.EndDate
                        && t.EndDate >= req.StartDate)
            .OrderBy(t => t.StartDate)
            .Select(t => new { t.Name, t.StartDate, t.EndDate })
            .FirstOrDefaultAsync(ct);

        if (clash is not null)
        {
            endpoint.ValidationFailures.Add(new ValidationFailure(nameof(TimeAwayRequest.StartDate),
                $"These dates overlap \"{clash.Name}\" ({clash.StartDate:yyyy-MM-dd} – {clash.EndDate:yyyy-MM-dd}). "
                + "Stretches of time away cannot overlap."));
            await endpoint.HttpContext.Response.SendErrorsAsync(endpoint.ValidationFailures, 400, cancellation: ct);
            return false;
        }

        return req.TemplateId is not { } templateId
               || await endpoint.EnsureOwnsDayTemplateAsync(dbContext, templateId, nameof(TimeAwayRequest.TemplateId), ct);
    }
}
