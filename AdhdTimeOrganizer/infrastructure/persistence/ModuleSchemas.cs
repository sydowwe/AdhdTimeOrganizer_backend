using System.Reflection;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Metadata;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.infrastructure.persistence;
using Sydowwe.Notifications.domain.entity;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Scheduler.domain.entity;
using CoreUser = AdhdTimeOrganizer.Core.domain.model.entity.user.User;

namespace AdhdTimeOrganizer.infrastructure.persistence;

/// <summary>
/// The database schema each module's tables live in, and the resolver <see cref="AppDbContext"/>
/// hands to <c>ApplySchemaPerModule</c>.
/// </summary>
/// <remarks>
/// One schema per slice, so that the project boundaries the solution already enforces in code are
/// visible in the database too — <c>\dt planning.*</c> is the slice's tables and nothing else.
/// <para>
/// Two schemas do not correspond to a project. <see cref="User"/> and <see cref="Activity"/> split
/// <c>AdhdTimeOrganizer.Core</c> up: Core is the one project every slice references, so a single
/// <c>core</c> schema would be a catch-all rather than a boundary. The split follows Core's own
/// entity namespaces — <c>entity.user</c> to <see cref="User"/>, <c>entity.activity</c> to
/// <see cref="Activity"/>.
/// </para>
/// <para>
/// Core's third namespace, <c>entity.timer</c>, stays in <see cref="Shared"/> (<c>public</c>)
/// alongside <c>__EFMigrationsHistory</c>. <c>public</c> also remains the model's default schema:
/// it is what Identity's own conventions fall back to before the sweep reassigns them, and what any
/// entity added without a map entry would otherwise land in silently — the resolver throws instead.
/// </para>
/// </remarks>
public static class ModuleSchemas
{
    /// <summary>The default schema. Holds <c>__EFMigrationsHistory</c> and Core's timer presets.</summary>
    public const string Shared = "public";

    public const string User = "user";
    public const string Activity = "activity";
    public const string TodoLists = "todo";
    public const string History = "history";
    public const string Planning = "planning";
    public const string Routines = "routines";
    public const string Tracking = "tracking";
    public const string ActivityProfiles = "activity_profiles";
    public const string Notifications = "notifications";
    public const string Reminders = "reminders";
    public const string Scheduler = "scheduler";

    /// <summary>
    /// Core splits by entity namespace. Anything not listed here — <c>entity.user</c> — is the user's.
    /// </summary>
    private static readonly Dictionary<string, string> SchemaByCoreNamespace = new()
    {
        ["AdhdTimeOrganizer.Core.domain.model.entity.activity"] = Activity,
        ["AdhdTimeOrganizer.Core.domain.model.entity.timer"] = Shared
    };

    /// <summary>
    /// Assembly → schema. Anchored on entity types rather than configuration types: an entity is the
    /// thing the schema is actually about, and unlike a configuration it cannot be left behind in the
    /// old project when a slice moves. Core is absent on purpose — it maps by namespace, below.
    /// </summary>
    private static readonly Dictionary<Assembly, string> SchemaByAssembly = new()
    {
        [typeof(TodoList).Assembly] = TodoLists,
        [typeof(ActivityHistory).Assembly] = History,
        [typeof(Calendar).Assembly] = Planning,
        [typeof(RoutineTodoList).Assembly] = Routines,
        [typeof(AndroidSessionData).Assembly] = Tracking,
        [typeof(MemoryAnchor).Assembly] = ActivityProfiles,
        [typeof(Notification).Assembly] = Notifications,
        [typeof(ReminderDefinition).Assembly] = Reminders,
        [typeof(ScheduledJob).Assembly] = Scheduler,

        // Framework's two mapped user-side entities, RefreshToken and UserRole. The third,
        // BusinessAuditLog, names the "audit" schema itself and never reaches the resolver.
        [typeof(RefreshToken).Assembly] = User,

        // Identity's satellite tables — user_claim, user_login, user_token, user__role,
        // user_role_claim. They describe the user and belong with it.
        [typeof(IdentityUserClaim<long>).Assembly] = User
    };

    /// <summary>
    /// The schema for one entity type. Throws for anything unmapped — see the remarks on
    /// <see cref="SchemaPerModuleConvention"/>; a fallback here would place a new slice's tables
    /// silently.
    /// </summary>
    public static string Resolve(IReadOnlyEntityType entityType)
    {
        var clrType = entityType.ClrType;

        if (clrType.Assembly == typeof(CoreUser).Assembly)
            return SchemaByCoreNamespace.GetValueOrDefault(clrType.Namespace ?? string.Empty, User);

        return SchemaByAssembly.TryGetValue(clrType.Assembly, out var schema)
            ? schema
            : throw new InvalidOperationException(
                $"No schema is mapped for entity type '{entityType.DisplayName()}' (assembly " +
                $"'{clrType.Assembly.GetName().Name}'). Add it to {nameof(ModuleSchemas)}.");
    }
}
