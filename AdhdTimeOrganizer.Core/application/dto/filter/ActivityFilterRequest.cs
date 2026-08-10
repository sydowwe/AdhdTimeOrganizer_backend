using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.Core.application.dto.filter;

public record ActivityFilterRequest : IFilterRequest
{
    public string? Name { get; set; }
    public string? Text { get; set; }
    public bool? IsUnavoidable { get; set; }

    public string? RoleName { get; set; }
    public string? CategoryName { get; set; }
    public long? RoleId { get; set; }
    public long? CategoryId { get; set; }
}