using AdhdTimeOrganizer.application.dto.response.activityHistory;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.webExtension.query;

public class GetAllWebExtensionDataEndpoint(
    AppCommandDbContext dbContext,
    WebExtensionDataMapper mapper)
    : BaseGetAllEndpoint<WebExtensionData, WebExtensionDataResponse, WebExtensionDataMapper>(dbContext, mapper)
{
    protected override IQueryable<WebExtensionData> WithIncludes(IQueryable<WebExtensionData> query)
    {
        return query
            .Include(wed => wed.Activity)
                .ThenInclude(a => a.Role)
            .Include(wed => wed.Activity)
                .ThenInclude(a => a.Category);
    }
}
