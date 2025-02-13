using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Command.domain.repositoryContract.activityPlanning;
using AdhdTimeOrganizer.Command.infrastructure.persistence;
using AdhdTimeOrganizer.Command.infrastructure.repository.@base;

namespace AdhdTimeOrganizer.Command.infrastructure.repository.activityPlanning;

public class ToDoListRepository(AppCommandDbContext context)
    : BaseEntityWithIsDoneRepository<ToDoList>(context), IToDoListRepository
{
}