namespace AdhdTimeOrganizer.application.dto.response.@base;

public record TextColorIconResponse : TextColorResponse
{
    public string? Icon { get; init; }
}