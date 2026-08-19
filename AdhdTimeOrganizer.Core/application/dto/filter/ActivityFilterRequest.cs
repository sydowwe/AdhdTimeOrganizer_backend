using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.Core.application.dto.filter;

public record ActivityFilterRequest : IFilterRequest
{
    public string? Name { get; set; }
    public string? Text { get; set; }
    public bool? IsUnavoidable { get; set; }

    /// <summary>
    /// <c>false</c> = active only, <c>true</c> = archived only, <c>null</c> = both. Backs the settings
    /// table's Active / Archived / All toggle.
    /// </summary>
    /// <remarks>
    /// ⚠ The default when this filter is <em>absent</em> is not <c>null</c>. A request with
    /// <c>useFilter: false</c> and no filter object — which is exactly what the table's unfiltered
    /// default view sends, and what it sent before A9 existed — must behave as <c>isArchived: false</c>,
    /// otherwise archived rows reappear in the one view most users never leave. That default cannot live
    /// on this property, because <c>BaseGridEndpoint</c> skips <c>ApplyCustomFiltering</c> entirely when
    /// no filter is sent; it lives in <c>GridActivityEndpoint.ApplyBaseFiltering</c>, which always runs.
    /// </remarks>
    public bool? IsArchived { get; set; }

    public string? RoleName { get; set; }
    public string? CategoryName { get; set; }
    public long? RoleId { get; set; }
    public long? CategoryId { get; set; }
}
