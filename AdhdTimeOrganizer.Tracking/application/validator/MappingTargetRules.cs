using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using FluentValidation;

namespace AdhdTimeOrganizer.Tracking.application.validator;

/// <summary>
/// The shape rules for a mapping's target group, in one copy across the four mapping validators (CQ-10).
/// </summary>
/// <remarks>
/// Both rules mirror a check constraint (<c>CK_Tracker*MappingByPattern_TargetRequired</c> and
/// <c>…_IsIgnoredOnlyTrue</c>), so without them the failure is a Postgres <c>23514</c> the client sees as
/// a 500 rather than a 400 naming what is wrong. They were transcribed on the create validators only for
/// as long as create was the only route that could write a target; the update DTOs carry the four members
/// as of CQ-10, and a transcription is what let the create/update pair drift in the first place.
/// </remarks>
public static class MappingTargetRules
{
    public static void AddMappingTargetRules<TRequest>(this AbstractValidator<TRequest> validator)
        where TRequest : IMappingTargetRequest
    {
        // Exactly one target group: IsIgnored=true | ActivityId | RoleId/CategoryId
        validator.RuleFor(x => x)
            .Must(x =>
            {
                var isIgnoredSet = x.IsIgnored == true;
                var activitySet = x.ActivityId != null;
                var roleOrCategorySet = x.RoleId != null || x.CategoryId != null;
                var targetsSet = new[] { isIgnoredSet, activitySet, roleOrCategorySet }.Count(v => v);
                return targetsSet == 1;
            })
            .WithMessage("Exactly one target must be set: IsIgnored=true, ActivityId, or RoleId/CategoryId (not multiple)")
            .OverridePropertyName("Target");

        // IsIgnored can only be true, never false
        validator.RuleFor(x => x.IsIgnored)
            .Must(v => v is null or true)
            .WithMessage("IsIgnored can only be set to true");
    }
}
