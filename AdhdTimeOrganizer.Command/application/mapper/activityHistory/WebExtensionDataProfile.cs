using AdhdTimeOrganizer.Command.application.dto.request;
using AdhdTimeOrganizer.Command.application.dto.response.activityHistory;
using AdhdTimeOrganizer.Command.domain.model.entity.activityHistory;
using AutoMapper;

namespace AdhdTimeOrganizer.Command.application.mapper.activityHistory;

public class WebExtensionDataProfile : Profile
{
    public WebExtensionDataProfile()
    {
        CreateMap<WebExtensionDataRequest, WebExtensionData>();
        CreateMap<WebExtensionData, WebExtensionDataResponse>();
    }
}