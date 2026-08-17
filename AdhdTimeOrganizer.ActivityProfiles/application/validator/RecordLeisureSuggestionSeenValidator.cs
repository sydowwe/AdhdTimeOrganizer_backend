using AdhdTimeOrganizer.ActivityProfiles.application.dto;
using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using AdhdTimeOrganizer.ActivityProfiles.domain.model;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.ActivityProfiles.application.validator;

public class RecordLeisureSuggestionSeenValidator : Validator<RecordLeisureSuggestionSeenRequest>
{
    /// <summary>
    /// A draw is three cards, and the biggest one this API will hand out is twenty. Bounded so a single request
    /// cannot ask the endpoint to walk an arbitrary key list.
    /// </summary>
    private const int MaxKeys = 20;

    public RecordLeisureSuggestionSeenValidator()
    {
        RuleFor(x => x.Keys).NotEmpty().Must(keys => keys.Count <= MaxKeys)
            .WithMessage($"At most {MaxKeys} keys per request.");

        // A malformed key is a client bug, not a stale bookmark, so it is worth a 400. A *well-formed* key
        // naming an activity that no longer exists is the ordinary case and the endpoint drops it silently.
        RuleForEach(x => x.Keys)
            .Must(key => LeisureSuggestionKey.TryParse(key, out _, out _))
            .WithMessage("Each key must look like \"<source>:<activityId>\", e.g. \"bucketList:8\".");

        RuleFor(x => x.Outcome)
            .Must(outcome => LeisureSuggestionTokens.TryParseOutcome(outcome, out _))
            .WithMessage("Outcome must be one of: rejected, committed.");
    }
}
