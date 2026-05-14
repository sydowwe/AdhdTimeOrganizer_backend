using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.activity;

public record ActivityCategoryResponse : NameTextColorIconResponse
{
    public string? Role { get; init; }
}