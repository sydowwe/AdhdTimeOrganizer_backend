using AdhdTimeOrganizer.Routines.application.dto.request.todoList;
using AdhdTimeOrganizer.Routines.application.validator;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineTimePeriod.command;

public class UpdateRoutineTimePeriodEndpoint(DbContext dbContext)
    : BaseUpdateEndpoint<RoutineTimePeriod, RoutineTimePeriodRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<RoutineTimePeriodValidator>();
    }
}