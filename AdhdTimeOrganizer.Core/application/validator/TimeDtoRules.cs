using AdhdTimeOrganizer.Core.application.dto.dto;
using FluentValidation;
using Sydowwe.Framework.application.dto.dto;

namespace AdhdTimeOrganizer.Core.application.validator;

/// <summary>
/// The range rule every request carrying a <see cref="TimeDto"/> needs. Attach it from the concrete
/// <c>FastEndpoints.Validator&lt;TRequest&gt;</c> that owns the request.
///
/// <para>It is an extension on the rule builder rather than a <c>Validator&lt;TimeDto&gt;</c> on purpose.
/// FastEndpoints resolves validators by the endpoint's <b>exact request type</b>, so a validator declared
/// over a DTO that is only ever a <i>member</i> of a request is never instantiated — it is dead code that
/// looks like coverage. That is exactly what <c>TimeDtoValidator</c> was, and it is the same trap
/// <see cref="DateRangeDto.ApplyValidationRules{T}"/> documents.</para>
///
/// <para>Unvalidated is not merely unchecked here: <see cref="TimeDto.ToTimeOnly"/> is
/// <c>new TimeOnly(Hours, Minutes)</c>, which throws <see cref="ArgumentOutOfRangeException"/>. A request
/// that reaches the handler with <c>hours: 99</c> is an unhandled 500 rather than a 400.</para>
/// </summary>
public static class TimeDtoRules
{
    /// <summary>
    /// Hours 0–23, minutes 0–59. A <c>null</c> passes — optionality is the caller's concern, expressed with
    /// <c>NotNull</c> or a <c>When</c>, so this rule never turns an optional member into a required one.
    /// </summary>
    public static IRuleBuilderOptions<T, TimeDto> ApplyTimeOfDayRules<T>(this IRuleBuilder<T, TimeDto> rule) =>
        rule.Must(t => t is null || (t.Hours is >= 0 and <= 23 && t.Minutes is >= 0 and <= 59))
            .WithMessage("{PropertyName} hours must be 0–23 and minutes 0–59.");

    /// <summary>
    /// Orders two times without going through <see cref="TimeDto.ToTimeOnly"/>.
    ///
    /// <para>The distinction matters inside a validator. FluentValidation runs every <c>RuleFor</c> chain,
    /// not just the ones up to the first failure, so a "start before end" rule written with
    /// <c>ToTimeOnly()</c> still executes on the request that the range rule above it just rejected — and
    /// throws out of the validator, turning the 400 it was about to produce into a 500. Minute-of-day
    /// arithmetic orders valid times identically and cannot throw; an out-of-range value simply picks up
    /// this message alongside its own.</para>
    /// </summary>
    public static bool IsBefore(this TimeDto start, TimeDto end) =>
        start.ToMinuteOfDay() < end.ToMinuteOfDay();

    private static int ToMinuteOfDay(this TimeDto time) => time.Hours * 60 + time.Minutes;
}
