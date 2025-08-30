using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityPlanning;

[Mapper]
public partial class ToDoListMapper : IBaseReadMapper<ToDoList, ToDoListResponse>
{
    public partial ToDoListResponse ToResponse(ToDoList entity);
    public partial SelectOptionResponse ToSelectOptionResponse(ToDoList entity);
}
