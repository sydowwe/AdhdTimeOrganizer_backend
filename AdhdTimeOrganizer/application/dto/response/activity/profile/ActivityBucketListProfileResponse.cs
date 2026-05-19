using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.activity.profile;

public record ActivityBucketListProfileResponse : IdResponse
{
    public required long ActivityId { get; init; }
    public required long ExperienceTypeId { get; init; }
    public required int ComfortZoneStep { get; init; }
    public required bool RequiresTravel { get; init; }
    public decimal? FinancialGoal { get; init; }
    public required string InspirationSource { get; init; }
}
