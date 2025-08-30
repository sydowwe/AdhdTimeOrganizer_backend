using AdhdTimeOrganizer.application.dto.request.filter;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning;

public class GetRoutineTimePeriodsFilteredPaginatedEndpoint(
    AppCommandDbContext dbContext,
    RoutineTimePeriodMapper mapper) 
    : BaseFilteredPaginatedEndpoint<RoutineTimePeriod, RoutineTimePeriodResponse, RoutineTimePeriodFilterRequest>(dbContext)
{
    private readonly RoutineTimePeriodMapper _mapper = mapper;

    protected override IQueryable<RoutineTimePeriod> ApplyCustomFiltering(IQueryable<RoutineTimePeriod> query, RoutineTimePeriodFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            query = query.Where(rtp => rtp.Text != null && rtp.Text.Contains(filter.Text));
        }

        if (!string.IsNullOrWhiteSpace(filter.Color))
        {
            query = query.Where(rtp => rtp.Color.Contains(filter.Color));
        }

        if (filter.MinLengthInDays.HasValue)
        {
            query = query.Where(rtp => rtp.LengthInDays >= filter.MinLengthInDays.Value);
        }

        if (filter.MaxLengthInDays.HasValue)
        {
            query = query.Where(rtp => rtp.LengthInDays <= filter.MaxLengthInDays.Value);
        }

        if (filter.IsHiddenInView.HasValue)
        {
            query = query.Where(rtp => rtp.IsHiddenInView == filter.IsHiddenInView.Value);
        }

        if (filter.UserId.HasValue)
        {
            query = query.Where(rtp => rtp.UserId == filter.UserId.Value);
        }

        return query;
    }

    protected override RoutineTimePeriodResponse MapToResponse(RoutineTimePeriod entity)
    {
        return _mapper.ToResponse(entity);
    }
}
