using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.generic;

public record SelectOptionRequest(string Text, int? SortOrder) : IUpdateRequest, ICreateRequest;