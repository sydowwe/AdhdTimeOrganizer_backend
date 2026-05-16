using AdhdTimeOrganizer.application.dto.request.@base;
using AdhdTimeOrganizer.application.dto.request.todoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class TodoListCategoryValidator : NameTextColorIconValidator<TodoListCategoryRequest>
{
}
