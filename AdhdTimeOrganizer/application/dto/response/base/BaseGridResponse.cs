namespace AdhdTimeOrganizer.application.dto.response.@base;

public record BaseGridResponse<TResponse> where TResponse : class, IIdResponse
{
    public required List<TResponse> Items { get; set; }
    public required int ItemsCount { get; set; }
    public required int PageCount { get; set; }
}