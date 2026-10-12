using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Sydowwe.Framework.domain.entity.@base;

namespace AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;

public class ActivityProjectProfile : BaseTableEntity, IActivityProfile
{
    public long ActivityId { get; set; }
    public Activity Activity { get; set; } = null!;

    public DifficultyLevel DifficultyLevel { get; set; }
    public string ProjectArea { get; set; } = null!;
    public decimal EstimatedHours { get; set; }
    public bool IsMessy { get; set; }
    public List<string> MaterialsNeeded { get; set; } = [];
    public List<string> RequiredTools { get; set; } = [];
    public ReadinessStatus ReadinessStatus { get; set; }

    /// <summary>
    /// The to-do item the shopping handoff created for this project, if any (FO13). Ticking that item done
    /// moves a <see cref="ReadinessStatus.NeedsShopping"/> project to <see cref="ReadinessStatus.ReadyToStart"/>
    /// — see <c>ProjectShoppingDoneEventHandler</c>.
    /// </summary>
    /// <remarks>
    /// A link, not an activity match: the handoff item is for the project's own activity, so keying on the
    /// activity would also fire on a "work on the bookshelf" to-do. Navigation-free, and its FK (to TodoLists'
    /// <c>TodoListItem</c>, <c>SetNull</c>) is declared host-side in <c>ConfigureCrossSliceRelationships</c>,
    /// because this slice cannot see TodoLists. One column rather than a link table: a project has one
    /// shopping trip at a time, and a second handoff replaces the first. Written only by
    /// <c>PatchActivityProjectProfileShoppingItemEndpoint</c> — <c>ActivityProjectProfileRequest.UpdateEntity</c>
    /// leaves it alone, or every save from the profile form would unlink it.
    /// </remarks>
    public long? ShoppingTodoListItemId { get; set; }
}