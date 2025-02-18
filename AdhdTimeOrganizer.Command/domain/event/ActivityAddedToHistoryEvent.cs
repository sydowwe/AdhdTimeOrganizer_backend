using AdhdTimeOrganizer.Command.domain.dto;
using MediatR;

namespace AdhdTimeOrganizer.Command.domain.@event;
public record ActivityAddedToHistoryEvent(ActivityHistoryCreatedDto NewActivityHistory) : INotification;