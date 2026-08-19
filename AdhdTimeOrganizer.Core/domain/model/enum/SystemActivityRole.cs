using System.Text.Json.Serialization;

namespace AdhdTimeOrganizer.Core.domain.model.@enum;

/// <summary>
/// The three activity roles the application itself references — the role a quick-created activity
/// lands under when it is made from a routine to-do, a normal to-do or a planner task.
///
/// <para><b>Why this exists.</b> The client used to find those roles by their seeded English display
/// name (<c>GET /activity-role/by-Name/Planner task</c>). The name is user-editable, so renaming one
/// — which the Slovak UI has to do — 404'd the lookup and silently killed quick-create. The key is
/// the identity; the name stays free text.</para>
///
/// <para>The member names are the wire contract, camelCase per
/// <see cref="JsonStringEnumMemberNameAttribute"/>, and double as i18n sub-keys client-side. They are
/// URL-safe and are sent verbatim as the path segment of
/// <c>GET /activity-role/by-system-key/{key}</c>. <b>Never rename one.</b> The persisted column holds
/// the C# member name instead (see <c>RoleConfiguration</c>), so the two spellings are independent.</para>
/// </summary>
public enum SystemActivityRole
{
    [JsonStringEnumMemberName("routineTask")]
    RoutineTask,

    [JsonStringEnumMemberName("todoListTask")]
    TodoListTask,

    [JsonStringEnumMemberName("plannerTask")]
    PlannerTask
}
