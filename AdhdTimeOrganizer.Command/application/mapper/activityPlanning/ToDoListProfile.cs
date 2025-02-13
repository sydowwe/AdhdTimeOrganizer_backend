using AdhdTimeOrganizer.Command.application.dto.request.toDoList;
using AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;
using AutoMapper;

namespace AdhdTimeOrganizer.Command.application.mapper.activityPlanning;

public class ToDoListProfile : Profile
{
    public ToDoListProfile()
    {
        CreateMap<ToDoListRequest, ToDoList>();
        CreateMap<ToDoList, ToDoListResponse>();
    }
}