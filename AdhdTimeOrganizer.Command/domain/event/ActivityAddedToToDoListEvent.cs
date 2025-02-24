using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using MediatR;

namespace AdhdTimeOrganizer.Command.domain.@event;

public record ActivityAddedToToDoListEvent(long ActivityId) : INotification;