using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.routineTimePeriod.query;

public class GetRoutineTimePeriodsFetchTableEndpoint(
    AppCommandDbContext dbContext,
    RoutineTimePeriodMapper mapper) 
    : BaseFetchTableEndpoint<RoutineTimePeriod, RoutineTimePeriodResponse, RoutineTimePeriodFilterRequest, RoutineTimePeriodMapper>(dbContext, mapper)
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

        if (filter.IsHidden.HasValue)
        {
            query = query.Where(rtp => rtp.IsHidden == filter.IsHidden.Value);
        }

        if (filter.UserId.HasValue)
        {
            query = query.Where(rtp => rtp.UserId == filter.UserId.Value);
        }

        return query;
    }
}
