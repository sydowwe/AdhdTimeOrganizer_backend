using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;

namespace AdhdTimeOrganizer.application.dto.request.activity.profile;

public record ActivityBucketListProfileRequest : IMyRequest<ActivityBucketListProfile>
{
    [Required] public long ActivityId { get; init; }
    [Required] public long ExperienceTypeId { get; init; }
    [Required] public int ComfortZoneStep { get; init; }
    [Required] public bool RequiresTravel { get; init; }
    public decimal? FinancialGoal { get; init; }
    [Required] public string InspirationSource { get; init; } = null!;

    public ActivityBucketListProfile ToEntity => new()
    {
        ActivityId = ActivityId,
        ExperienceTypeId = ExperienceTypeId,
        ComfortZoneStep = ComfortZoneStep,
        RequiresTravel = RequiresTravel,
        FinancialGoal = FinancialGoal,
        InspirationSource = InspirationSource,
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
