namespace AdhdTimeOrganizer.application.dto.request.generic;

public record ToggleIsDoneRequest(
    List<long> Ids,
    bool? ForceValue = null
);