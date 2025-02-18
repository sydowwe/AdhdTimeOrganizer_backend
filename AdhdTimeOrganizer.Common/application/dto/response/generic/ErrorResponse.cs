using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.Common.application.dto.response.generic;

public record ErrorResponse
{
    public required string Error { get; init; }
    public required string Message { get; init; }
}