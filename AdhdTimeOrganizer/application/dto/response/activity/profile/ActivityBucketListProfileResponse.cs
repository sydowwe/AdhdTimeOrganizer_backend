using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;


namespace AdhdTimeOrganizer.application.dto.response.activity.profile;

public record ActivityBucketListProfileResponse : IdResponse, IProjectionResponse<ActivityBucketListProfileResponse, ActivityBucketListProfile>
{
    public required long ActivityId { get; init; }
    public required long ExperienceTypeId { get; init; }
    public required int ComfortZoneStep { get; init; }
    public required bool RequiresTravel { get; init; }
    public decimal? FinancialGoal { get; init; }
    public required string InspirationSource { get; init; }

    public static IQueryable<ActivityBucketListProfileResponse> Projection(IQueryable<ActivityBucketListProfile> query) =>
        query.Select(e => new ActivityBucketListProfileResponse
        {
            Id = e.Id,
            ActivityId = e.ActivityId,
            ExperienceTypeId = e.ExperienceTypeId,
            ComfortZoneStep = e.ComfortZoneStep,
            RequiresTravel = e.RequiresTravel,
            FinancialGoal = e.FinancialGoal,
            InspirationSource = e.InspirationSource,
        });

    public static ActivityBucketListProfileResponse FromEntity(ActivityBucketListProfile e) =>
        new()
        {
            Id = e.Id,
            ActivityId = e.ActivityId,
            ExperienceTypeId = e.ExperienceTypeId,
            ComfortZoneStep = e.ComfortZoneStep,
            RequiresTravel = e.RequiresTravel,
            FinancialGoal = e.FinancialGoal,
            InspirationSource = e.InspirationSource,
        };
}
