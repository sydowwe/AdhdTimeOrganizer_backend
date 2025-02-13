using AdhdTimeOrganizer.Command.application.dto.request;
using AdhdTimeOrganizer.Command.application.dto.response.activityHistory;
using AdhdTimeOrganizer.Command.application.@interface.@base;
using AdhdTimeOrganizer.Command.application.service.@base;
using AdhdTimeOrganizer.Command.domain.model.entity.activityHistory;

namespace AdhdTimeOrganizer.Command.application.@interface.activityHistory;

public interface IWebExtensionDataService : IEntityWithActivityService<WebExtensionData, WebExtensionDataRequest, WebExtensionDataResponse>
{
}