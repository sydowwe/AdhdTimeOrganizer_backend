using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.webExtension.query;

public class GetSelectOptionsWebExtensionDataEndpoint(
    AppCommandDbContext appDbContext,
    WebExtensionDataMapper mapper)
    : BaseGetSelectOptionsEndpoint<WebExtensionData, WebExtensionDataMapper>(appDbContext, mapper)
{
}
