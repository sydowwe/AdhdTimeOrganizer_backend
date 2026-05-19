using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.activity.profile;

public record ActivityBucketListProfileRequest : ICreateRequest, IUpdateRequest
{
    [Required] public long ActivityId { get; init; }
    [Required] public long ExperienceTypeId { get; init; }
    [Required] public int ComfortZoneStep { get; init; }
    [Required] public bool RequiresTravel { get; init; }
    public decimal? FinancialGoal { get; init; }
    [Required] public string InspirationSource { get; init; } = null!;
}
