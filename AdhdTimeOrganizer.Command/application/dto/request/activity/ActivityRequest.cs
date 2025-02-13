using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Command.application.dto.request.extendable;

namespace AdhdTimeOrganizer.Command.application.dto.request.activity;

public record ActivityRequest(
    string Name,
    string Text,
    [ Required] bool IsOnToDoList,
    [ Required] bool IsUnavoidable,
    [ Required] long RoleId,
    long? CategoryId,
    long? ToDoListUrgencyId
) : NameTextRequest(Name, Text);