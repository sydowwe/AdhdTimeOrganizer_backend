using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.Planning.domain.model.entity.suggestion;

/// <summary>
/// A keyless mapping over <c>mv_template_suggestion_pattern</c>. A plain POCO carrying exactly the view's five
/// columns, like its two siblings — <b>not</b> a <c>BaseEntityWithUser</c>.
/// <para>
/// It was one until 2026-09-08, and that made every call to <c>GetSuggestionsTaskPlannerDayTemplateEndpoint</c>
/// answer <b>500</b>: the base contributes <c>Id</c>, <c>CreatedTimestamp</c>, <c>ModifiedTimestamp</c> and a
/// row version, EF selected them, and an aggregate view has no such columns
/// (<c>42703: column m.created_timestamp does not exist</c>). Nothing caught it because the endpoint had no
/// test at all; <c>TaskPlannerDayTemplateSuggestionsTests</c> is now that test.
/// </para>
/// <para>
/// Consequence worth knowing: like the other two view mappings, this type is outside the global user query
/// filter, so the endpoint's own <c>p.UserId == userId</c> predicate is the only thing scoping it.
/// </para>
/// </summary>
public class PlannerSuggestionFromDayTemplate
{
    public long UserId { get; set; }
    public long TemplateId { get; set; }
    public TaskPlannerDayTemplate Template { get; set; } = null!;
    public int PatternType { get; set; } // 0=DayOfWeek, 1=DayType
    public int PatternValue { get; set; } // 1–7 (DOW) or 0–4 (DayType enum int)
    public int OccurrenceCount { get; set; }
}
