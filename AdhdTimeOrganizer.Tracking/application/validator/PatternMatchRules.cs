using System.Text.RegularExpressions;
using FluentValidation;
using Sydowwe.Framework.domain.@enum;

namespace AdhdTimeOrganizer.Tracking.application.validator;

/// <summary>
/// The syntax rule for a <see cref="PatternMatchType.Regex"/> filter value, in one copy across the grids
/// that take a match type (<c>CQ-12</c>).
/// </summary>
/// <remarks>
/// <c>Regex</c> used to reach the database as a SQL <c>LIKE</c>, so a pattern that is not a regex at all
/// was simply a filter that matched nothing. It is now the Postgres <c>~*</c> operator, and an
/// unparseable pattern is an error from the server the endpoint's catch-all would report as a 500. This
/// turns it into the 400 it is — the pattern came from the user.
/// <para>
/// The check is .NET's parser, which is not Postgres' ARE parser: a pattern both engines accept behaves
/// the same, but .NET-only syntax (named groups, <c>\d</c> inside a bracket expression) still fails at
/// the database. It is the same parser the ingest side compiles user patterns with, so a pattern this
/// accepts is one a mapping rule could hold.
/// </para>
/// </remarks>
public static class PatternMatchRules
{
    public static IRuleBuilderOptions<TRequest, string?> MustBeValidRegexWhenRegexMatch<TRequest>(
        this IRuleBuilder<TRequest, string?> ruleBuilder,
        Func<TRequest, PatternMatchType?> matchType) =>
        ruleBuilder
            .Must((request, value) => matchType(request) != PatternMatchType.Regex || IsParseableRegex(value))
            .WithMessage("'{PropertyName}' is not a valid regular expression");

    private static bool IsParseableRegex(string? pattern)
    {
        if (pattern == null)
            return true;

        try
        {
            _ = new Regex(pattern);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
