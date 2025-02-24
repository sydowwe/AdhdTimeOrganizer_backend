using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using MediatR;

namespace AdhdTimeOrganizer.Command.domain.@event;

public record ActivityAddedToRoutineToDoListEvent(long ActivityId) : INotification;