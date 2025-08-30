using AdhdTimeOrganizer.application.dto.response.activityHistory;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper;

[Mapper]
public partial class WebExtensionDataMapper : IBaseReadMapper<WebExtensionData, WebExtensionDataResponse>
{
    public partial WebExtensionDataResponse ToResponse(WebExtensionData entity);
    public partial SelectOptionResponse ToSelectOptionResponse(WebExtensionData entity);
}
