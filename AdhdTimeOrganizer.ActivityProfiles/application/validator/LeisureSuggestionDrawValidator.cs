using AdhdTimeOrganizer.ActivityProfiles.application.dto;
using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.ActivityProfiles.application.validator;

public class LeisureSuggestionDrawValidator : Validator<LeisureSuggestionDrawRequest>
{
    /// <summary>A day. Beyond it the constraint has stopped describing an afternoon.</summary>
    private const int MaxMinutes = 24 * 60;

    /// <summary>
    /// The picker asks for three and the rule caps a source at one; a caller asking for hundreds would be
    /// asking for a list, which is the thing this endpoint exists not to be.
    /// </summary>
    private const int MaxCount = 20;

    /// <summary>uint32 — the range the client's seed generator works in, and what the jitter consumes.</summary>
    private const long MaxSeed = 4294967295L;

    public LeisureSuggestionDrawValidator()
    {
        RuleFor(x => x.Minutes).InclusiveBetween(0, MaxMinutes);
        RuleFor(x => x.People).InclusiveBetween(1, 100);
        RuleFor(x => x.Count).InclusiveBetween(1, MaxCount);
        RuleFor(x => x.Seed).InclusiveBetween(0, MaxSeed);
        RuleFor(x => x.MaxCostTierId).GreaterThan(0).When(x => x.MaxCostTierId.HasValue);
        RuleFor(x => x.LocationTypeId).GreaterThan(0).When(x => x.LocationTypeId.HasValue);

        // Rejected rather than defaulted: energy decides most of the ordering, so a typo that silently became
        // "medium" would hand the user a draw they did not ask for and no error to explain it.
        RuleFor(x => x.Energy)
            .Must(energy => LeisureSuggestionTokens.TryParseEnergy(energy, out _))
            .WithMessage("Energy must be one of: low, medium, high.");
    }
}
