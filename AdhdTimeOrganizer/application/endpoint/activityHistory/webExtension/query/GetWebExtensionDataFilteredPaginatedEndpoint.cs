using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.activityHistory;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.webExtension.query;

public class GetWebExtensionDataFilteredPaginatedEndpoint(
    AppCommandDbContext dbContext,
    WebExtensionDataMapper mapper) 
    : BaseFilteredPaginatedEndpoint<WebExtensionData, WebExtensionDataResponse, WebExtensionDataFilterRequest>(dbContext)
{
    private readonly WebExtensionDataMapper _mapper = mapper;

    protected override IQueryable<WebExtensionData> WithIncludes(IQueryable<WebExtensionData> query)
    {
        return query
            .Include(wed => wed.Activity)
                .ThenInclude(a => a.Role)
            .Include(wed => wed.Activity)
                .ThenInclude(a => a.Category);
    }

    protected override IQueryable<WebExtensionData> ApplyCustomFiltering(IQueryable<WebExtensionData> query, WebExtensionDataFilterRequest filter)
    {
        if (filter.ActivityId.HasValue)
        {
            query = query.Where(wed => wed.ActivityId == filter.ActivityId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Domain))
        {
            query = query.Where(wed => wed.Domain.Contains(filter.Domain));
        }

        if (!string.IsNullOrWhiteSpace(filter.Title))
        {
            query = query.Where(wed => wed.Title.Contains(filter.Title));
        }

        if (filter.MinDuration.HasValue)
        {
            query = query.Where(wed => wed.Duration >= filter.MinDuration.Value);
        }

        if (filter.MaxDuration.HasValue)
        {
            query = query.Where(wed => wed.Duration <= filter.MaxDuration.Value);
        }

        if (filter.StartTimestampAfter.HasValue)
        {
            query = query.Where(wed => wed.StartTimestamp >= filter.StartTimestampAfter.Value);
        }

        if (filter.StartTimestampBefore.HasValue)
        {
            query = query.Where(wed => wed.StartTimestamp <= filter.StartTimestampBefore.Value);
        }

        if (filter.UserId.HasValue)
        {
            query = query.Where(wed => wed.UserId == filter.UserId.Value);
        }

        return query;
    }

    protected override WebExtensionDataResponse MapToResponse(WebExtensionData entity)
    {
        return _mapper.ToResponse(entity);
    }
}
