using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.request;

public record ActivityBucketListProfileRequest : IMyRequest<ActivityBucketListProfile>
{
    public long ActivityId { get; init; }
    public long ExperienceTypeId { get; init; }
    public int ComfortZoneStep { get; init; }
    public bool RequiresTravel { get; init; }
    public decimal? FinancialGoal { get; init; }
    public string InspirationSource { get; init; } = null!;

    public ActivityBucketListProfile ToEntity => new()
    {
        ActivityId = ActivityId,
        ExperienceTypeId = ExperienceTypeId,
        ComfortZoneStep = ComfortZoneStep,
        RequiresTravel = RequiresTravel,
        FinancialGoal = FinancialGoal,
        InspirationSource = InspirationSource
    };

    public void UpdateEntity(ActivityBucketListProfile e)
    {
        e.ExperienceTypeId = ExperienceTypeId;
        e.ComfortZoneStep = ComfortZoneStep;
        e.RequiresTravel = RequiresTravel;
        e.FinancialGoal = FinancialGoal;
        e.InspirationSource = InspirationSource;
    }
}