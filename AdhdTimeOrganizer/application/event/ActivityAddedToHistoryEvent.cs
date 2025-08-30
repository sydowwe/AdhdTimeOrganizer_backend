using AdhdTimeOrganizer.domain.model.dto;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.@event;
public record ActivityAddedToHistoryEvent(ActivityHistoryCreatedDto NewActivityHistory) : IEvent;