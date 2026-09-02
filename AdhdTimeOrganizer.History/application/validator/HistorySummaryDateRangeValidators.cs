using AdhdTimeOrganizer.Core.application.dto.dto;
using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.History.application.dto.request.activityHistory.dashboard.summary;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.History.application.validator;

/// <summary>
/// Carries <see cref="DateRangeDto.ApplyValidationRules{T}"/> onto one request type.
///
/// <para>The four subclasses below look like boilerplate and are not: FastEndpoints resolves a validator by
/// the request's <b>exact</b> type, so this base — abstract and open-generic — is never discovered, and any
/// <c>summary/</c> request without its own concrete subclass goes through unvalidated. That failure is
/// silent in the way this repo's <c>CLAUDE.md</c> warns about: the endpoint keeps answering 200, just for a
/// range nobody asked for. A fifth range-taking endpoint needs a fifth line here, and
/// <c>HistorySummaryCustomRangeTests.EveryFourthSummaryEndpoint_Rejects…</c> is what notices when it is
/// missing.</para>
/// </summary>
public abstract class DateRangeRequestValidator<TRequest> : Validator<TRequest>
    where TRequest : DateRangeDto
{
    protected DateRangeRequestValidator() => DateRangeDto.ApplyValidationRules(this, x => x);
}

/// <summary>
/// The one summary request that carries more than a date range. <c>WindowStartTime</c> /
/// <c>WindowEndTime</c> reach <c>TimeDto.ToTimeOnly()</c> in the endpoint, which throws on an out-of-range
/// value — so without these rules <c>{"hours": 99}</c> is a 500, not the 400 it is now.
/// </summary>
public class HistorySummaryStackedBarsRequestValidator
    : DateRangeRequestValidator<HistorySummaryStackedBarsRequest>
{
    public HistorySummaryStackedBarsRequestValidator()
    {
        RuleFor(x => x.WindowStartTime).ApplyTimeOfDayRules();
        RuleFor(x => x.WindowEndTime).ApplyTimeOfDayRules();
    }
}

public class HistorySummaryPieChartRequestValidator
    : DateRangeRequestValidator<HistorySummaryPieChartRequest>;

/// <summary>
/// <c>Baseline</c> is an enum on the wire, so a made-up string is already a 400 from the binder. This rule
/// closes the other door: a numeric body value (<c>"baseline": 99</c>) binds to an undefined member, which
/// <c>HistoryBaselineWindow</c> now throws on rather than silently reading as <c>last7days</c> — a 400 is
/// the right answer to it, not a 500.
/// </summary>
public class HistorySummarySummaryCardsRequestValidator
    : DateRangeRequestValidator<HistorySummarySummaryCardsRequest>
{
    public HistorySummarySummaryCardsRequestValidator()
    {
        RuleFor(x => x.Baseline).IsInEnum();
    }
}

public class HistorySummaryTimeOfDayRequestValidator
    : DateRangeRequestValidator<HistorySummaryTimeOfDayRequest>;
