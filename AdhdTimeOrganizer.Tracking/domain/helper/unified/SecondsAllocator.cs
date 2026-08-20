namespace AdhdTimeOrganizer.Tracking.domain.helper.unified;

/// <summary>
/// Turns the merge's fractional seconds into whole ones <b>without letting the parts stop adding up</b>.
///
/// <para>The overlap rule hands a losing source a share of a minute, so its seconds come out
/// fractional. Rounding each item on its own would drift: a source with two hundred items could round
/// up on most of them and report a hundred seconds more than it was credited with, and the two
/// arithmetic checks the unified page invites — the chips summing to the pie's total, and
/// <c>countedSeconds + displacedSeconds</c> matching the source's own dashboard — would fail by a
/// visible margin on exactly the busy days a user would bother to check.</para>
///
/// <para>Largest-remainder instead: every value is floored, and the seconds left over go to the values
/// that lost the most in the flooring. The parts then sum to the whole exactly, and no single item is
/// off by more than a second.</para>
/// </summary>
public static class SecondsAllocator
{
    /// <summary>
    /// Allocates <paramref name="target"/> whole seconds across <paramref name="values"/> in proportion
    /// to them. <paramref name="target"/> defaults to the rounded sum, which is the only thing any
    /// caller here wants.
    /// </summary>
    public static int[] Allocate(IReadOnlyList<double> values, int? target = null)
    {
        var result = new int[values.Count];

        if (values.Count == 0)
            return result;

        var goal = target ?? (int)Math.Round(values.Sum(), MidpointRounding.AwayFromZero);
        var remainders = new (int Index, double Remainder)[values.Count];

        for (var i = 0; i < values.Count; i++)
        {
            var floor = (int)Math.Floor(values[i]);
            result[i] = floor;
            remainders[i] = (i, values[i] - floor);
        }

        var shortfall = goal - result.Sum();

        if (shortfall > 0)
        {
            // Ties fall to the earlier index, so the same input always allocates the same way and a
            // response does not flicker between two equally valid roundings.
            foreach (var (index, _) in remainders
                         .OrderByDescending(r => r.Remainder)
                         .ThenBy(r => r.Index)
                         .Take(shortfall))
                result[index]++;
        }
        else if (shortfall < 0)
        {
            foreach (var (index, _) in remainders
                         .Where(r => result[r.Index] > 0)
                         .OrderBy(r => r.Remainder)
                         .ThenBy(r => r.Index)
                         .Take(-shortfall))
                result[index]--;
        }

        return result;
    }
}
