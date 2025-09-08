using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@base;

namespace AdhdTimeOrganizer.application.dto.request.activity;

public record QuickEditActivityRequest : NameTextRequest
{
    public long? CategoryId { get; init; }
}