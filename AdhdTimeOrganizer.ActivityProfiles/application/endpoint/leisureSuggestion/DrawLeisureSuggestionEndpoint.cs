using AdhdTimeOrganizer.ActivityProfiles.application.dto;
using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using AdhdTimeOrganizer.ActivityProfiles.application.dto.response;
using AdhdTimeOrganizer.ActivityProfiles.application.validator;
using AdhdTimeOrganizer.ActivityProfiles.domain.model;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.ActivityProfiles.domain.service;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.leisureSuggestion;

/// <summary>
/// The leisure draw: a ranked handful of things to do right now, taken from the backlog, project and
/// bucket-list profiles together.
///
/// <para><b>Why this is a server endpoint and not three table reads.</b> The picker used to fetch the three
/// tables in full — capped at an arbitrary 200 rows each — and rank them in the browser, which meant a user
/// with a large backlog was silently ranked over a prefix of it, and three cards cost three whole table
/// downloads. Ranking across sources needs all three in one place, and that place is here.</para>
///
/// <para><b>The draw remembers what it showed.</b> That record used to live in <c>localStorage</c>, so
/// rerolling on a phone did not stop the laptop offering the same three. It is now
/// <c>LeisureSuggestionRecord</c> — the half of this feature that could not be fixed client-side at all.</para>
///
/// <para><b>POST for a read, deliberately.</b> The body is a constraint set rather than a resource path, and
/// it is the same shape the three <c>filtered-table</c> endpoints this replaces already take.</para>
///
/// <para>The rule itself is <see cref="LeisureDrawRanker"/>. This endpoint only decides which rows reach it,
/// and turns what comes back into cards.</para>
/// </summary>
public class DrawLeisureSuggestionEndpoint(DbContext dbContext)
    : Endpoint<LeisureSuggestionDrawRequest, LeisureSuggestionDrawResponse>
{
    /// <summary>
    /// One candidate, split into the facts the rule reads and the one fact only the card reads. Keeping them
    /// apart is what stops the ranking from quietly starting to depend on a label.
    /// </summary>
    private sealed record DrawRow(LeisureCandidate Candidate, string? ContextLabel);

    public override void Configure()
    {
        Post("/leisure-suggestion");
        Roles(this.GetUserRole());
        Validator<LeisureSuggestionDrawValidator>();
        Summary(s =>
        {
            s.Summary = "Draw a ranked set of leisure suggestions";
            s.Description = "Ranks the user's backlog, project and bucket-list profiles together under the given "
                            + "constraints and returns at most `count` of them, best first. Deterministic in "
                            + "(body, data, suggestion history).";
            s.Response<LeisureSuggestionDrawResponse>(200, "The draw — possibly empty");
            s.Response(400, "Invalid constraints");
        });
    }

    public override async Task HandleAsync(LeisureSuggestionDrawRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        // The validator has already rejected anything else, so the discarded false is unreachable rather than
        // lenient — a bad token never becomes a silent "low".
        LeisureSuggestionTokens.TryParseEnergy(req.Energy, out var energy);
        var constraints = new LeisureDrawConstraints(req.Minutes, energy, req.People);

        // Before any constraint — including the source floors that keep a whole table out of a short draw.
        // This is what tells "you have nothing filed yet" apart from "nothing matches"; a count taken after
        // filtering would make the picker blame the user for the filter.
        var poolCount = await CountPoolAsync(userId, ct);

        var rows = new List<DrawRow>();
        rows.AddRange(await ReadBacklogAsync(userId, req, ct));
        // A source that cannot contribute under this time budget is not queried at all. The eligibility rule
        // would drop every one of its rows anyway, so this is the same answer in fewer round trips.
        if (req.Minutes >= LeisureDrawRanker.ProjectMinMinutes)
            rows.AddRange(await ReadProjectsAsync(userId, ct));
        if (req.Minutes >= LeisureDrawRanker.BucketListMinMinutes)
            rows.AddRange(await ReadBucketListAsync(userId, ct));

        var context = new LeisureRankingContext(
            constraints,
            await ReadHistoryAsync(userId, ct),
            await ReadLastCommittedEffortAsync(userId, ct),
            DateTime.UtcNow,
            // The seed is a uint32 the client keeps in the page URL; the validator pins it to that range, so
            // this cast never loses a bit it was given.
            unchecked((uint)req.Seed));

        var (chosen, eligibleCount) = LeisureDrawRanker.Draw(rows.Select(row => row.Candidate), context, req.Count);

        // Only the chosen few need a name and an avatar, so the activity read is the last thing that happens
        // and covers three rows rather than the whole pool.
        var activities = await ReadActivityInfoAsync(userId, chosen.Select(candidate => candidate.ActivityId), ct);
        var byKey = rows.ToDictionary(row => row.Candidate.Key, StringComparer.Ordinal);

        await Send.OkAsync(new LeisureSuggestionDrawResponse
        {
            Items = chosen
                .Where(candidate => activities.ContainsKey(candidate.ActivityId))
                .Select(candidate => Map(byKey[candidate.Key], activities[candidate.ActivityId]))
                .ToList(),
            PoolCount = poolCount,
            EligibleCount = eligibleCount
        }, ct);
    }

    private static LeisureSuggestionItemResponse Map(DrawRow row, ActivityInfoResponse activity) => new()
    {
        Key = row.Candidate.Key,
        Source = LeisureSuggestionKey.Token(row.Candidate.Source),
        ActivityId = row.Candidate.ActivityId,
        Activity = activity,
        StatedDurationMinutes = row.Candidate.StatedDurationMinutes,
        MaxUsefulMinutes = row.Candidate.MaxUsefulMinutes,
        EnergyLevel = LeisureSuggestionTokens.Token(row.Candidate.EnergyLevel),
        EnergyIsDerived = row.Candidate.EnergyIsDerived,
        EffortType = LeisureSuggestionTokens.Token(row.Candidate.EffortType),
        MinParticipants = row.Candidate.MinParticipants,
        ReadinessStatus = row.Candidate.ReadinessStatus is { } status ? LeisureSuggestionTokens.Token(status) : null,
        ComfortZoneStep = row.Candidate.ComfortZoneStep,
        RequiresTravel = row.Candidate.RequiresTravel,
        ContextLabel = row.ContextLabel
    };

    private async Task<int> CountPoolAsync(long userId, CancellationToken ct) =>
        await BacklogOf(userId).CountAsync(ct)
        + await ProjectsOf(userId).CountAsync(ct)
        + await BucketListOf(userId).CountAsync(ct);

    // ─── the three sources ───────────────────────────────────────────────────────────────────────────
    //
    // Scoped by hand through p.Activity.UserId, exactly as the three profile grids are: the Activity*Profile
    // entities are not IEntityWithUser, so no global query filter backs these reads up and that traversal is
    // the only thing keeping another user's profiles out of this user's draw.

    private IQueryable<ActivityBacklogProfile> BacklogOf(long userId) =>
        dbContext.Set<ActivityBacklogProfile>().Where(p => p.Activity.UserId == userId);

    private IQueryable<ActivityProjectProfile> ProjectsOf(long userId) =>
        dbContext.Set<ActivityProjectProfile>().Where(p => p.Activity.UserId == userId);

    private IQueryable<ActivityBucketListProfile> BucketListOf(long userId) =>
        dbContext.Set<ActivityBucketListProfile>().Where(p => p.Activity.UserId == userId);

    private async Task<List<DrawRow>> ReadBacklogAsync(long userId, LeisureSuggestionDrawRequest req, CancellationToken ct)
    {
        var query = BacklogOf(userId);

        if (req.LocationTypeId is { } locationTypeId)
            query = query.Where(p => p.LocationTypeId == locationTypeId);

        // Cost tiers are user-editable rows, so "no more expensive than this one" is an ordering question and
        // not an id comparison — SortOrder is the field that exists for exactly this. An id that is not in the
        // user's tiers any more (a deleted tier still sitting in a bookmarked URL) must not silently become
        // "free only": no cutoff resolves, so no cost constraint is applied.
        if (req.MaxCostTierId is { } maxCostTierId)
        {
            var cutoff = await dbContext.Set<ActivityExpectedCostTier>()
                .Where(t => t.UserId == userId && t.Id == maxCostTierId)
                .Select(t => (int?)t.SortOrder)
                .FirstOrDefaultAsync(ct);

            if (cutoff is { } maxSortOrder)
                query = query.Where(p => p.ExpectedCostTier.SortOrder <= maxSortOrder);
        }

        var rows = await query
            .Select(p => new
            {
                p.ActivityId,
                p.EnergyLevel,
                p.EffortType,
                p.MinParticipants,
                p.DurationMinutes,
                ContextLabel = p.LocationType.Text
            })
            .ToListAsync(ct);

        return rows.Select(row =>
        {
            // A backlog row with no duration recorded is not a row that takes no time — it is one the user
            // never filled in. It must not be excluded as "0 minutes does not fit", nor shown as "takes 0 min".
            var duration = row.DurationMinutes > 0 ? row.DurationMinutes : (int?)null;
            return new DrawRow(
                new LeisureCandidate(
                    LeisureSuggestionSource.Backlog,
                    row.ActivityId,
                    duration,
                    duration,
                    row.EnergyLevel,
                    EnergyIsDerived: false,
                    row.EffortType,
                    row.MinParticipants,
                    ReadinessStatus: null,
                    ComfortZoneStep: null,
                    RequiresTravel: false),
                NullIfBlank(row.ContextLabel));
        }).ToList();
    }

    private async Task<List<DrawRow>> ReadProjectsAsync(long userId, CancellationToken ct)
    {
        var rows = await ProjectsOf(userId)
            // "Needs shopping" is an errand, not a suggestion — dropped before it crosses the wire, and the one
            // hard constraint of the rule the database can express on its own.
            .Where(p => p.ReadinessStatus != ReadinessStatus.NeedsShopping)
            .Select(p => new
            {
                p.ActivityId,
                p.DifficultyLevel,
                p.ReadinessStatus,
                p.EstimatedHours,
                ContextLabel = p.ProjectArea
            })
            .ToListAsync(ct);

        return rows.Select(row => new DrawRow(
            new LeisureCandidate(
                LeisureSuggestionSource.Project,
                row.ActivityId,
                StatedDurationMinutes: null,
                row.EstimatedHours > 0 ? (int)Math.Round(row.EstimatedHours * 60) : null,
                LeisureDrawRanker.DifficultyAsEnergy(row.DifficultyLevel),
                EnergyIsDerived: true,
                EffortType: null,
                MinParticipants: null,
                row.ReadinessStatus,
                ComfortZoneStep: null,
                RequiresTravel: false),
            NullIfBlank(row.ContextLabel))).ToList();
    }

    private async Task<List<DrawRow>> ReadBucketListAsync(long userId, CancellationToken ct)
    {
        var rows = await BucketListOf(userId)
            .Select(p => new
            {
                p.ActivityId,
                p.ComfortZoneStep,
                p.RequiresTravel,
                ContextLabel = p.ExperienceType.Text
            })
            .ToListAsync(ct);

        return rows.Select(row => new DrawRow(
            new LeisureCandidate(
                LeisureSuggestionSource.BucketList,
                row.ActivityId,
                StatedDurationMinutes: null,
                // These rows record no duration at all, so there is nothing to size a slot from — the client
                // falls back to the time the user said they had.
                MaxUsefulMinutes: null,
                LeisureDrawRanker.ComfortStepAsEnergy(row.ComfortZoneStep),
                EnergyIsDerived: true,
                EffortType: null,
                MinParticipants: null,
                ReadinessStatus: null,
                row.ComfortZoneStep,
                row.RequiresTravel),
            NullIfBlank(row.ContextLabel))).ToList();
    }

    /// <summary>
    /// The name and avatar for the drawn candidates, in one read — an activity carrying both a backlog and a
    /// project profile is fetched once. Scoped by <c>UserId</c> as well as by id: an activity that is not the
    /// caller's cannot become a card even if a profile row somehow pointed at it.
    /// <para>
    /// The category is optional and the role never is, which is why the role is the fallback for the icon and
    /// the colour — an uncategorised activity would otherwise render as a blank avatar.
    /// </para>
    /// </summary>
    private async Task<Dictionary<long, ActivityInfoResponse>> ReadActivityInfoAsync(
        long userId, IEnumerable<long> activityIds, CancellationToken ct)
    {
        var ids = activityIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var infos = await dbContext.Set<Activity>()
            .Where(a => a.UserId == userId && ids.Contains(a.Id))
            .Select(a => new ActivityInfoResponse(
                a.Id,
                a.Name,
                a.Category != null ? a.Category.Name : null,
                a.Category != null && a.Category.Icon != null ? a.Category.Icon : a.Role.Icon,
                a.Category != null ? a.Category.Color : a.Role.Color))
            .ToListAsync(ct);

        return infos.ToDictionary(info => info.Id);
    }

    /// <summary>
    /// Candidate key → when it was last put in front of this user. Read whole rather than filtered down to the
    /// candidate keys: it is one row per activity the user has ever been shown, the retention sweep on the
    /// write endpoint keeps it that size, and one read beats a parameter list as long as the pool.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, DateTime>> ReadHistoryAsync(long userId, CancellationToken ct)
    {
        var records = await dbContext.Set<LeisureSuggestionRecord>()
            .Where(r => r.UserId == userId)
            .Select(r => new { r.Source, r.ActivityId, r.LastSuggestedAt })
            .ToListAsync(ct);

        return records.ToDictionary(
            r => LeisureSuggestionKey.Format(r.Source, r.ActivityId),
            r => r.LastSuggestedAt,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The effort type of the last suggestion the user actually committed to — what the variety bonus varies
    /// away from. Only the backlog records an effort type, so committing to a project or a bucket-list entry
    /// legitimately resets this to null, and so does committing to a backlog row that records none.
    /// </summary>
    private async Task<EffortType?> ReadLastCommittedEffortAsync(long userId, CancellationToken ct)
    {
        var lastCommitted = await dbContext.Set<LeisureSuggestionRecord>()
            .Where(r => r.UserId == userId && r.LastOutcome == LeisureSuggestionOutcome.Committed)
            .OrderByDescending(r => r.LastSuggestedAt)
            .Select(r => new { r.Source, r.ActivityId })
            .FirstOrDefaultAsync(ct);

        if (lastCommitted is not { Source: LeisureSuggestionSource.Backlog })
            return null;

        var committedActivityId = lastCommitted.ActivityId;
        return await BacklogOf(userId)
            .Where(p => p.ActivityId == committedActivityId)
            .Select(p => p.EffortType)
            .FirstOrDefaultAsync(ct);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
