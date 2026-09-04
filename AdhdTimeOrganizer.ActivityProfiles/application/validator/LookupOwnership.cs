using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using FluentValidation;

namespace AdhdTimeOrganizer.ActivityProfiles.application.validator;

/// <summary>
/// The failure half of the profile lookup-ownership check, shared by the create and the update
/// validators so a message or a field name can't drift between the two halves of one write surface.
///
/// <para><b>Why the check exists.</b> The four <c>Activity*</c> lookups are
/// <c>BaseLookupWithUser</c> — every user has their own set — but the profile carries them as raw
/// <c>long</c> FK columns and the profiles themselves are not <c>IEntityWithUser</c>. The FK enforces
/// that the row exists, not that it is the caller's, so a create/update naming a stranger's
/// sequential id inserted cleanly: it pinned a row the victim then could not delete (all these FKs
/// are <c>DeleteBehavior.Restrict</c>), came back with a null label through the user-filtered
/// navigation, and answered differently for a real foreign id than for a non-existent one.</para>
///
/// <para>Same message for "does not exist" and "is not yours", or the endpoint becomes an existence
/// oracle over another account's id range — the rule the tracker-mapping guard (Tracking SEC-2)
/// already follows.</para>
///
/// <para>The queries live at the call sites, not here: EF cannot translate a helper method inside a
/// projection, and each validator folds the <c>Any</c>s into the round-trip it was already making.</para>
/// </summary>
internal static class LookupOwnership
{
    private const string NotFound = "Lookup not found.";

    public static void AddBacklogFailures<T>(ValidationContext<T> ctx, bool ownsLocationType, bool ownsWeatherDependency, bool ownsExpectedCostTier)
    {
        if (!ownsLocationType)
            ctx.AddFailure(nameof(ActivityBacklogProfileRequest.LocationTypeId), NotFound);

        if (!ownsWeatherDependency)
            ctx.AddFailure(nameof(ActivityBacklogProfileRequest.WeatherDependencyId), NotFound);

        if (!ownsExpectedCostTier)
            ctx.AddFailure(nameof(ActivityBacklogProfileRequest.ExpectedCostTierId), NotFound);
    }

    public static void AddBucketListFailures<T>(ValidationContext<T> ctx, bool ownsExperienceType)
    {
        if (!ownsExperienceType)
            ctx.AddFailure(nameof(ActivityBucketListProfileRequest.ExperienceTypeId), NotFound);
    }
}
