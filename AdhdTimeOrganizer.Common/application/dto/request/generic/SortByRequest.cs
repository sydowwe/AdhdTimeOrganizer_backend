using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.Common.application.dto.request.generic;

public record SortByRequest(
    [Required] string Key,
    bool IsDesc = false
);