using System.Text.RegularExpressions;
using Sydowwe.Framework.domain.@enum;

namespace AdhdTimeOrganizer.Tracking.domain.helper.mapping;

/// <summary>
/// Matches one tracked value against one user-authored pattern. The <c>Regex</c> and <c>Wildcard</c>
/// patterns run <em>entries × active mappings</em> times per heartbeat, so each one is parsed once per
/// instance and every match is bounded by <see cref="MatchTimeout"/> — without that, one catastrophic
/// pattern (<c>(a+)+$</c> against a long window title) would pin a request thread on every heartbeat the
/// agent posts. A pattern that times out counts as "no match".
/// </summary>
/// <remarks>
/// This used to be a private class of <c>DesktopActivityHeartbeatEndpoint</c>. It is the string-level
/// half of the matcher; the per-table halves — which columns a rule constrains and in what order the
/// rules are walked — are <see cref="DesktopMappingRuleSet"/> and <see cref="AndroidMappingRuleSet"/>.
/// One instance per request: the regex cache is per instance and not thread-safe.
/// </remarks>
public sealed class PatternMatcher
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    private readonly Dictionary<(string Pattern, PatternMatchType MatchType), Regex> compiled = new();

    public bool Matches(string? value, string pattern, PatternMatchType matchType)
    {
        if (value == null)
            return false;
        return matchType switch
        {
            PatternMatchType.Exact => string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase),
            PatternMatchType.Contains => value.Contains(pattern, StringComparison.OrdinalIgnoreCase),
            PatternMatchType.Wildcard or PatternMatchType.Regex => IsRegexMatch(value, pattern, matchType),
            _ => false
        };
    }

    private bool IsRegexMatch(string value, string pattern, PatternMatchType matchType)
    {
        try
        {
            return GetRegex(pattern, matchType).IsMatch(value);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private Regex GetRegex(string pattern, PatternMatchType matchType)
    {
        if (compiled.TryGetValue((pattern, matchType), out var regex))
            return regex;

        var expression = matchType == PatternMatchType.Wildcard
            ? "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$"
            : pattern;

        regex = new Regex(expression, RegexOptions.IgnoreCase, MatchTimeout);
        compiled[(pattern, matchType)] = regex;
        return regex;
    }
}
