namespace AdhdTimeOrganizer.infrastructure.persistence;

/// <summary>
/// Names of this host's own global query filters (EF 10 named filters). The per-user concern is not
/// here — it belongs to the Framework, which applies it to every <c>IEntityWithUser</c>; reference
/// <c>Sydowwe.Framework.infrastructure.persistence.QueryFilters.UserScope</c> for that one, including
/// on the entities <see cref="AppDbContext"/> scopes by hand.
///
/// <para><b>One name per concern, not per entity.</b> Every entity carrying the same concern carries
/// the same name, so a single <c>IgnoreQueryFilters([AppQueryFilters.ActivityArchived])</c> lifts it
/// on the principal and its dependents together. Naming them per entity would leave the other end of
/// a join filtered, which surfaces as a row that loads with an empty include rather than as an error.</para>
///
/// <para>Always pass the constant, never the literal: EF does not validate that a name handed to
/// <c>IgnoreQueryFilters</c> matches a filter that exists, so a typo silently ignores nothing.</para>
/// </summary>
public static class AppQueryFilters
{
    /// <summary>
    /// Hides archived activities — <c>!IsArchived</c> on <c>Activity</c>, and the mirrored
    /// <c>!Activity.IsArchived</c> on every dependent that must disappear with its activity.
    /// Ignore it on surfaces that exist to show archived rows (an archive grid, an un-archive
    /// endpoint, a merge that has to find both sides).
    /// </summary>
    public const string ActivityArchived = "ActivityArchived";

    /// <summary>
    /// The retention bound on <c>WebExtensionActivityEntry</c> — <c>RecordDate &gt;=
    /// AppDbContext.CurrentPartitionDate</c>, which keeps reads inside the live partitions.
    /// Ignoring it makes queries touch the archived partitions, so do it only for an explicit
    /// historical export, never on a request path.
    /// </summary>
    public const string PartitionDate = "PartitionDate";

    /// <summary>
    /// The hand-rolled scope on <c>RoutinePeriodCompletion</c> — mirrored through its required
    /// <c>RoutineTimePeriod</c> navigation because the completion row itself carries no <c>UserId</c>
    /// (<c>BaseEntity</c>, not <c>BaseEntityWithUser</c>, so it never reaches
    /// <c>ApplyUserQueryFilters</c>). Deliberately its own name rather than
    /// <c>Sydowwe.Framework...QueryFilters.UserScope</c>: every currently-scoped entity is scoped
    /// through the real <c>UserId</c> path, so lifting them together with a single
    /// <c>IgnoreQueryFilters([QueryFilters.UserScope])</c> is safe. This filter walks a navigation
    /// instead, and the one caller that needs unscoped completions
    /// (<c>RoutineTodoListResetJobHandler</c>, which runs with <c>ScopeUserId == null</c> anyway) must
    /// not be able to accidentally also lift every other user filter in the same call — naming it
    /// separately makes that impossible.
    /// </summary>
    public const string RoutinePeriodCompletionUserScope = "RoutinePeriodCompletionUserScope";
}
