using AdhdTimeOrganizer.Command.application.dto.request.toDoList;
using AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;
using AutoMapper;

namespace AdhdTimeOrganizer.Command.application.mapper.activityPlanning;

public class RoutineToDoListProfile : Profile
{
    public RoutineToDoListProfile()
    {
        CreateMap<RoutineToDoListRequest, RoutineToDoList>();
        CreateMap<RoutineToDoList, RoutineToDoListResponse>();
    }
}