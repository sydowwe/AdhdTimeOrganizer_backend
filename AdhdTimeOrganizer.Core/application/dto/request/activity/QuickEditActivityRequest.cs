using Sydowwe.Framework.application.dto.request.@base;

namespace AdhdTimeOrganizer.Core.application.dto.request.activity;

public record QuickEditActivityRequest : NameTextRequest
{
    public long? CategoryId { get; init; }
}