namespace AdhdTimeOrganizer.Planning.domain.service;

/// <summary>
/// FO4: the one rule that puts blocks into a day's free time — the re-plan of the rest of today packs many with
/// it, the next-free-slot read places one. Pure arithmetic over <b>minutes of one day</b> (0 … 1440), no SQL; what
/// counts as occupied, and each block's earliest start and latest end, are the caller's (<c>DayReplanPlanner</c>).
/// <list type="bullet">
/// <item><b>First fit, in priority order.</b> Each block, in the order given, takes the earliest free stretch that
/// starts no earlier than its <see cref="Item.EarliestStart"/>, ends no later than its <see cref="Item.LatestEnd"/>
/// and is long enough to hold it. Placing it uses that stretch up. So a short block later in the order may take a
/// gap an earlier, longer one could not fit in: the order is a priority, not a promise that the blocks keep their
/// sequence on the clock.</item>
/// <item><b>A block keeps its length</b> and is never split. One longer than every free stretch does not fit.</item>
/// <item><b>Optional blocks give way first.</b> When a <i>required</i> block does not fit, the day is packed again
/// with every required block ahead of every optional one, and that packing wins if it places more required blocks.
/// Optional blocks then take whatever is left, in their own order. When only optional blocks miss out, the
/// in-order packing stands.</item>
/// <item><b>What does not fit</b> comes back optional first, then in the order given — the order to let go in.</item>
/// </list>
/// </summary>
public static class DayPacking
{
    public const int MinutesPerDay = 24 * 60;

    /// <summary>
    /// The latest minute a planner block can end on: 23:59. A block is a pair of times of one day with its end after
    /// its start (<c>PlannerTaskValidator</c>), and a <c>TimeDto</c> carries hours 0–23, so "midnight" is not an end
    /// a block can have. The client caps a moved block at the same minute (<c>useTodayPlan.ts</c>).
    /// </summary>
    public const int LastEndMinute = MinutesPerDay - 1;

    /// <summary>A half-open stretch of the day, <c>[Start, End)</c>, in minutes.</summary>
    public readonly record struct Span(int Start, int End)
    {
        public int Length => End - Start;
    }

    /// <summary>A block to place.</summary>
    /// <param name="Id">The caller's key for it.</param>
    /// <param name="Duration">Its length in minutes; a block of zero or less never fits.</param>
    /// <param name="EarliestStart">The first minute it may start on.</param>
    /// <param name="LatestEnd">The last minute it may end on.</param>
    /// <param name="IsOptional">Whether it gives way to required blocks when the day runs out.</param>
    public sealed record Item(long Id, int Duration, int EarliestStart, int LatestEnd, bool IsOptional);

    /// <summary>Where each placed block went, and the blocks that did not fit (optional first, then in order).</summary>
    public sealed record Result(IReadOnlyDictionary<long, Span> Placed, IReadOnlyList<long> WontFit);

    public static int MinuteOf(TimeOnly time) => time.Hour * 60 + time.Minute;

    /// <summary>The first whole minute at or after <paramref name="time"/> — 1440 for anything past 23:59.</summary>
    public static int CeilingMinuteOf(TimeOnly time) =>
        (int)((time.Ticks + TimeSpan.TicksPerMinute - 1) / TimeSpan.TicksPerMinute);

    /// <summary>A minute of the day as a time. Valid for 0 … 1439.</summary>
    public static TimeOnly TimeOf(int minute) => new(minute / 60, minute % 60);

    /// <summary>
    /// The last minute a day's blocks may end on, from its sleep window: bed time, unless bed time is at or before
    /// wake-up time — 00:00 (the default bed time, <c>UserSleepWindow.Fallback</c>) or any time after
    /// midnight — in which case the day's own end, <see cref="LastEndMinute"/>. A block cannot cross midnight, so
    /// a bed time after it clips to the end of the day rather than reaching into the next one.
    /// </summary>
    public static int WindowEnd(TimeOnly wakeUpTime, TimeOnly bedTime) =>
        bedTime <= wakeUpTime ? LastEndMinute : Math.Min(MinuteOf(bedTime), LastEndMinute);

    /// <summary>
    /// The stretch of the day a block holds: <c>[start, end)</c>, rounded outwards to whole minutes. An end before
    /// the start wrapped past midnight (rows that predate <c>PlannerTaskValidator</c>'s ordering rule), so it holds
    /// the rest of this day; an end equal to the start holds nothing.
    /// </summary>
    public static Span Occupancy(TimeOnly startTime, TimeOnly endTime) =>
        endTime < startTime
            ? new Span(MinuteOf(startTime), MinutesPerDay)
            : new Span(MinuteOf(startTime), Math.Max(MinuteOf(startTime), CeilingMinuteOf(endTime)));

    /// <summary>Packs <paramref name="items"/>, in the order given, into the day around <paramref name="occupied"/>.</summary>
    public static Result Pack(IReadOnlyCollection<Span> occupied, IReadOnlyList<Item> items)
    {
        var optional = items.Where(i => i.IsOptional).Select(i => i.Id).ToHashSet();

        var chosen = FirstFit(occupied, items);
        if (chosen.WontFit.Any(id => !optional.Contains(id)))
        {
            var requiredFirst = FirstFit(occupied, items.Where(i => !i.IsOptional).Concat(items.Where(i => i.IsOptional)));
            if (RequiredPlaced(requiredFirst) > RequiredPlaced(chosen))
                chosen = requiredFirst;
        }

        var position = items.Select((item, index) => (item.Id, index)).ToDictionary(p => p.Id, p => p.index);
        var wontFit = chosen.WontFit
            .OrderBy(id => optional.Contains(id) ? 0 : 1)
            .ThenBy(id => position[id])
            .ToList();

        return chosen with { WontFit = wontFit };

        int RequiredPlaced(Result result) => result.Placed.Keys.Count(id => !optional.Contains(id));
    }

    /// <summary>
    /// The earliest free stretch of <paramref name="duration"/> minutes around <paramref name="occupied"/>, starting
    /// no earlier than <paramref name="earliestStart"/> and ending no later than <paramref name="latestEnd"/> — the
    /// same rule <see cref="Pack"/> applies, for a single block. <c>null</c> when none is long enough.
    /// </summary>
    public static Span? FirstFreeSlot(IReadOnlyCollection<Span> occupied, int duration, int earliestStart, int latestEnd) =>
        Take(FreeSpans(occupied), duration, earliestStart, latestEnd);

    private static Result FirstFit(IReadOnlyCollection<Span> occupied, IEnumerable<Item> items)
    {
        var free = FreeSpans(occupied);
        var placed = new Dictionary<long, Span>();
        var wontFit = new List<long>();

        foreach (var item in items)
        {
            if (Take(free, item.Duration, item.EarliestStart, item.LatestEnd) is { } slot)
                placed[item.Id] = slot;
            else
                wontFit.Add(item.Id);
        }

        return new Result(placed, wontFit);
    }

    /// <summary>The day minus <paramref name="occupied"/>, as sorted, disjoint stretches.</summary>
    private static List<Span> FreeSpans(IEnumerable<Span> occupied)
    {
        var free = new List<Span>();
        var cursor = 0;
        foreach (var block in occupied.Where(b => b.Length > 0).OrderBy(b => b.Start))
        {
            if (block.Start > cursor)
                free.Add(new Span(cursor, Math.Min(block.Start, MinutesPerDay)));
            cursor = Math.Max(cursor, block.End);
        }

        if (cursor < MinutesPerDay)
            free.Add(new Span(cursor, MinutesPerDay));
        return free;
    }

    /// <summary>Takes the earliest fitting stretch out of <paramref name="free"/> (kept sorted), or returns null.</summary>
    private static Span? Take(List<Span> free, int duration, int earliestStart, int latestEnd)
    {
        if (duration <= 0)
            return null;

        for (var i = 0; i < free.Count; i++)
        {
            var gap = free[i];
            var start = Math.Max(gap.Start, earliestStart);
            var end = start + duration;

            // Every later gap starts later still, so nothing after this one can end in time either.
            if (end > latestEnd)
                return null;
            if (end > gap.End)
                continue;

            free.RemoveAt(i);
            if (end < gap.End)
                free.Insert(i, new Span(end, gap.End));
            if (gap.Start < start)
                free.Insert(i, new Span(gap.Start, start));
            return new Span(start, end);
        }

        return null;
    }
}
