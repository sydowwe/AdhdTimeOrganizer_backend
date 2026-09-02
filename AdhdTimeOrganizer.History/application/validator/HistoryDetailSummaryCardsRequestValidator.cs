using AdhdTimeOrganizer.History.application.dto.request.activityHistory.dashboard.detail;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.History.application.validator;

/// <summary>
/// The <c>detail/</c> twin of the <c>Baseline</c> rule in
/// <see cref="HistorySummarySummaryCardsRequestValidator"/>, and the only validator on a <c>detail/</c>
/// request today. Concrete on purpose, like every validator in this slice: FastEndpoints resolves one by
/// the request's <b>exact</b> type, so an abstract or open-generic one is never discovered and the
/// endpoint keeps answering 200.
///
/// <para>It deliberately does <b>not</b> restate the range rules the <c>summary/</c> validators get from
/// <c>DateRangeDto.ApplyValidationRules</c> — the <c>detail/</c> requests carry a
/// <c>DateAndTimeRangeDto</c>, which is a separate gap, not something this class quietly half-fixes.</para>
/// </summary>
public class HistoryDetailSummaryCardsRequestValidator : Validator<HistoryDetailSummaryCardsRequest>
{
    public HistoryDetailSummaryCardsRequestValidator()
    {
        RuleFor(x => x.Baseline).IsInEnum();
    }
}
