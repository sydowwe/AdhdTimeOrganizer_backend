using AdhdTimeOrganizer.ActivityProfiles.application.dto;
using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using AdhdTimeOrganizer.ActivityProfiles.application.validator;
using AdhdTimeOrganizer.ActivityProfiles.domain.model;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.leisureSuggestion;

/// <summary>
/// Records what became of a draw — which is the input the next draw's staleness reads.
///
/// <para>The client sends <c>rejected</c> for the whole outgoing set when the user presses "something else",
/// and <c>committed</c> for the single one they planned. It deliberately sends nothing when a draw is merely
/// rendered: recording on render would mean reloading the page demoted the very cards on it, and the seeded
/// URL would stop reproducing its own draw.</para>
///
/// <para><b>Upsert, keyed on (user, source, activity).</b> "When was this last shown" has exactly one answer,
/// so a second reroll of the same card moves the timestamp rather than adding a row.</para>
/// </summary>
public class RecordSeenLeisureSuggestionEndpoint(DbContext dbContext, ILogger<RecordSeenLeisureSuggestionEndpoint> logger)
    : Endpoint<RecordLeisureSuggestionSeenRequest>
{
    /// <summary>
    /// Past this, "last suggested" has stopped meaning anything and is only making the table bigger — the
    /// staleness signal already saturates about a week out, and GDPR Art. 5(1)(e) does not much like rows kept
    /// for reasons nobody can name. Swept on write rather than by a scheduled job: the sweep is one indexed
    /// DELETE for the one user who is writing, so it costs nothing and cannot silently stop running.
    /// </summary>
    private const int RetentionDays = 90;

    public override void Configure()
    {
        Post("/leisure-suggestion/seen");
        Roles(this.GetUserRole());
        Validator<RecordLeisureSuggestionSeenValidator>();
        Summary(s =>
        {
            s.Summary = "Record the outcome of a leisure draw";
            s.Description = "Marks the given candidate keys as shown, and how they went. Idempotent.";
            s.Response(204, "Recorded");
            s.Response(400, "Malformed key or unknown outcome");
        });
    }

    public override async Task HandleAsync(RecordLeisureSuggestionSeenRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        // Both already checked by the validator; a failure here cannot reach a write.
        LeisureSuggestionTokens.TryParseOutcome(req.Outcome, out var outcome);

        var targets = req.Keys
            .Select(key => LeisureSuggestionKey.TryParse(key, out var source, out var activityId)
                ? new { Source = source, ActivityId = activityId }
                : null)
            .Where(target => target is not null)
            .Select(target => target!)
            .Distinct()
            .ToList();

        // A key naming an activity that is not the caller's — or one that has since been deleted — is dropped
        // rather than rejected. Keys come back from a draw the user may have had open for days, so a stale one
        // is ordinary; and an insert against a foreign activity id would fail on the FK as a 500 anyway.
        var activityIds = targets.Select(target => target.ActivityId).ToList();
        var ownedIds = await dbContext.Set<Activity>()
            .Where(a => a.UserId == userId && activityIds.Contains(a.Id))
            .Select(a => a.Id)
            .ToListAsync(ct);

        var owned = targets.Where(target => ownedIds.Contains(target.ActivityId)).ToList();
        var now = DateTime.UtcNow;

        if (owned.Count > 0)
        {
            var existing = await dbContext.Set<LeisureSuggestionRecord>()
                .Where(r => r.UserId == userId && activityIds.Contains(r.ActivityId))
                .ToListAsync(ct);

            var added = new List<LeisureSuggestionRecord>();

            foreach (var target in owned)
            {
                var record = existing.FirstOrDefault(r => r.Source == target.Source && r.ActivityId == target.ActivityId);
                if (record is null)
                {
                    record = new LeisureSuggestionRecord
                    {
                        UserId = userId,
                        ActivityId = target.ActivityId,
                        Source = target.Source
                    };
                    dbContext.Set<LeisureSuggestionRecord>().Add(record);
                    added.Add(record);
                }

                record.LastSuggestedAt = now;
                record.LastOutcome = outcome;
            }

            await SaveAsync(userId, activityIds, added, now, outcome, ct);
        }

        await PruneAsync(userId, now, ct);
        await Send.NoContentAsync(ct);
    }

    /// <summary>
    /// (user_id, source, activity_id) is unique, so two writes racing onto the same key — a double-tap on
    /// "something else", or a phone and a laptop rerolling at once — leave one of them here. The row it wanted
    /// now exists, so the loser stamps that one instead of failing a write the user has every right to make.
    /// Both writes were recording the same fact, so the winner's value is the right one either way.
    /// </summary>
    private async Task SaveAsync(long userId, List<long> activityIds, List<LeisureSuggestionRecord> added,
        DateTime now, LeisureSuggestionOutcome outcome, CancellationToken ct)
    {
        try
        {
            await dbContext.SaveChangesAsync(ct);
            return;
        }
        catch (DbUpdateException)
        {
            // A failed insert stays Added on the tracker and would be retried by the next SaveChanges.
            foreach (var record in added)
                dbContext.Entry(record).State = EntityState.Detached;
        }

        var winners = await dbContext.Set<LeisureSuggestionRecord>()
            .Where(r => r.UserId == userId && activityIds.Contains(r.ActivityId))
            .ToListAsync(ct);

        foreach (var record in added)
        {
            var winner = winners.FirstOrDefault(r => r.Source == record.Source && r.ActivityId == record.ActivityId);
            if (winner is null)
                throw new InvalidOperationException(
                    $"Lost the race recording a leisure suggestion for user {userId}, but the winning row is not there.");

            winner.LastSuggestedAt = now;
            winner.LastOutcome = outcome;
        }

        logger.LogDebug("Lost the race recording {Count} leisure suggestion(s) for user {UserId}; stamping the existing rows",
            added.Count, userId);

        await dbContext.SaveChangesAsync(ct);
    }

    private Task PruneAsync(long userId, DateTime now, CancellationToken ct)
    {
        // Computed here rather than inside the predicate: EF would otherwise try to translate AddDays into SQL.
        var cutoff = now.AddDays(-RetentionDays);
        return dbContext.Set<LeisureSuggestionRecord>()
            .Where(r => r.UserId == userId && r.LastSuggestedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
