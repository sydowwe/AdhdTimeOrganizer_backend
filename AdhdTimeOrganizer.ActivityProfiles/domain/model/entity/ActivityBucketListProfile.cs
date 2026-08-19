using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Sydowwe.Framework.domain.entity.@base;

namespace AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;

public class ActivityBucketListProfile : BaseTableEntity, IActivityProfile
{
    public long ActivityId { get; set; }
    public Activity Activity { get; set; } = null!;

    public long ExperienceTypeId { get; set; }
    public ActivityExperienceType ExperienceType { get; set; } = null!;
    public int ComfortZoneStep { get; set; }
    public bool RequiresTravel { get; set; }
    public decimal? FinancialGoal { get; set; }
    public string InspirationSource { get; set; } = null!;
}