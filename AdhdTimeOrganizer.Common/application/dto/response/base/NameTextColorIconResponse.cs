namespace AdhdTimeOrganizer.Common.application.dto.response.@base;

public record NameTextColorIconResponse : NameTextColorResponse
{
    public string? Icon { get; init; }
}