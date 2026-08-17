using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;

namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.request;

/// <summary>
/// Status-only patch for a project profile's readiness. The readiness board flips this one enum per
/// click, so it does not round-trip the whole <see cref="ActivityProjectProfileRequest"/>.
/// </summary>
public record PatchActivityProjectProfileStatusRequest
{
    public required ReadinessStatus ReadinessStatus { get; init; }
}
