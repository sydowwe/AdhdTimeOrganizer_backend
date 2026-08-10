using AdhdTimeOrganizer.application.dto.request.todoList;
using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTimePeriod.command;

public class CreateRoutineTimePeriodEndpoint(AppDbContext dbContext)
    : BaseCreateEndpoint<RoutineTimePeriod, RoutineTimePeriodRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<RoutineTimePeriodValidator>();
    }
}