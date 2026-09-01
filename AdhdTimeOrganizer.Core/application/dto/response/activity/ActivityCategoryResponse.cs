using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;

namespace AdhdTimeOrganizer.Core.application.dto.response.activity;

public record ActivityCategoryResponse : NameTextColorIconResponse, IProjectionResponse<ActivityCategoryResponse, ActivityCategory>
{
    /// <summary>
    /// How many activities are filed under this category, <b>archived ones included</b>. Note this differs
    /// from <c>ActivityCategoryGridResponse.Role</c>, which is archive-filtered because it is a display
    /// column rather than a blast radius.
    /// </summary>
    /// <remarks>
    /// ⚠ Same "default means not asked for" contract as <c>ActivityResponse.UsageCount</c> — only
    /// <c>GridActivityCategoryEndpoint</c> and <c>GetByIdActivityCategoryEndpoint</c> fill this in;
    /// elsewhere, including the category nested inside every <c>ActivityResponse</c>, it is <c>0</c> and
    /// means nothing.
    /// </remarks>
    public int UsageCount { get; init; }

    /// <summary>
    /// Nothing is filed under this category.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>This one is advisory, not a prediction of a 409.</b> <c>Activity.CategoryId</c> is optional and
    /// the FK is <c>SetNull</c>, so deleting a used category is not destructive — it blanks the
    /// classification and leaves every activity and its history intact, and the endpoint answers 204. The
    /// field exists so the settings grid can say <em>what</em> a delete will reclassify before it happens,
    /// which is the whole gap this replaces. Contrast <c>ActivityRoleResponse.CanDelete</c>, which the role
    /// endpoint really does enforce.
    /// </remarks>
    public bool CanDelete { get; init; }

    public static IQueryable<ActivityCategoryResponse> Projection(IQueryable<ActivityCategory> query)
    {
        return query.Select(e => new ActivityCategoryResponse { Id = e.Id, Name = e.Name, Text = e.Text, Color = e.Color, Icon = e.Icon });
    }
}