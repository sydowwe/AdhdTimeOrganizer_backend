namespace AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;

public record StepItemAndStepIdRequest(
    long ItemId,
    Guid StepId
);