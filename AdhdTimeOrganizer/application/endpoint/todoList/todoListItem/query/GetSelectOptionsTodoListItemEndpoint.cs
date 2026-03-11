using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using TodoListItemMapper = AdhdTimeOrganizer.application.mapper.todoList.TodoListItemMapper;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListItem.query;

public class GetSelectOptionsTodoListItemEndpoint(AppDbContext appDbContext, TodoListItemMapper mapper)
    : BaseGetSelectOptionsEndpoint<TodoListItem, TodoListItemMapper>(appDbContext, mapper);
