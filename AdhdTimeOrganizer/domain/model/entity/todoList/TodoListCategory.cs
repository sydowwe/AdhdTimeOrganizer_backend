using AdhdTimeOrganizer.domain.model.entity.@base;

namespace AdhdTimeOrganizer.domain.model.entity.todoList;

public class TodoListCategory : BaseNameTextColorIconEntity
{
    public ICollection<TodoList> TodoListColl { get; set; } = new List<TodoList>();
}
