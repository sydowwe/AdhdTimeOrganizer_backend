using System.Reflection;
using AdhdTimeOrganizer.IntegrationTests.Endpoints;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.reminders;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Modules;

/// <summary>
/// The payload PII contract (<see cref="INotificationPayload"/>): a persisted notification or reminder payload
/// carries ids and non-person scalars, never a person's name, address, phone, e-mail, birth number or IBAN. The
/// marker interface makes an anonymous object impossible; this makes a person-data <i>property</i> on a record a red
/// test, so the contract also holds for the field added tomorrow.
/// <para>
/// It walks <b>this solution's</b> records, wherever they live. Payloads used to sit in one framework file, but a
/// kind and its payload now live in the slice that raises them, so a guard that only looked at Contracts would
/// quietly stop covering them. The assemblies are taken from the loaded set, the <c>SeamWiringTests</c> way: the
/// fixture has booted the real host by the time this runs, and the coverage check below fails if a slice's records
/// were not seen.
/// </para>
/// </summary>
[Collection("Postgres")]
public class PayloadPiiContractGuardTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    /// <summary>
    /// Fields whose name the rule would refuse but whose content is the recipient's <b>own</b> label for their
    /// <b>own</b> row — the <c>PersonalReminderPayload.Title</c> / <c>TimerBoundaryPayload.ActivityName</c>
    /// precedent. Each is a stored JSON key, so renaming it would strand every row already written.
    /// </summary>
    private static readonly HashSet<string> AllowedOwnLabels =
    [
        // A routine item's activity name, in the user's own suggested-today digest to themselves.
        "RoutineSuggestedTodayItem.Name"
    ];

    private static IEnumerable<Type> PayloadTypes =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name is { } name
                        && (name.StartsWith("AdhdTimeOrganizer", StringComparison.Ordinal) || name.StartsWith("Sydowwe.", StringComparison.Ordinal))
                        && name != "AdhdTimeOrganizer.IntegrationTests"
                        && !name.EndsWith(".Testing", StringComparison.Ordinal))
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && (typeof(INotificationPayload).IsAssignableFrom(t) || typeof(IReminderPayload).IsAssignableFrom(t)))
            .Distinct();

    [Fact(DisplayName = "No payload record in the solution carries a person-data property, at any depth")]
    public void NoPayload_CarriesPersonData()
    {
        var offending = new List<string>();
        foreach (var type in PayloadTypes)
            Walk(type, type.Name, [], offending);

        offending.Should().BeEmpty(
            "a payload row belongs to its recipient, so a name in it is frozen PII that survives GDPR erasure — persist the id "
            + "and resolve the name at render time (INotificationPayload, PayloadPersonDataNames)");
    }

    /// <summary>
    /// The scan sees every catalogued kind's payload record, so a slice whose records it missed — an assembly it did
    /// not walk — is a red test here rather than an unguarded payload.
    /// </summary>
    [Fact(DisplayName = "The guard sees the payload record of every kind this host raises")]
    public void Scan_SeesEveryCataloguedKindsPayload()
    {
        var seen = PayloadTypes
            .Select(t => t.GetCustomAttribute<NotificationPayloadAttribute>()?.Type)
            .OfType<Sydowwe.Framework.Contracts.notification.NotificationType>()
            .ToList();

        seen.Should().Contain(NotificationCatalogTests.Catalog);
        seen.Should().OnlyHaveUniqueItems("two payload records claiming one kind make the typed NotifyAsync ambiguous");
    }

    private static void Walk(Type type, string path, HashSet<Type> visiting, List<string> offending)
    {
        if (!visiting.Add(type))
            return;

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var qualified = $"{type.Name}.{property.Name}";
            if (PayloadPersonDataNames.IsForbidden(property.Name) && !AllowedOwnLabels.Contains(qualified))
                offending.Add($"{path}.{property.Name}");

            if (NestedRecord(property.PropertyType) is { } nested)
                Walk(nested, $"{path}.{property.Name}", visiting, offending);
        }

        visiting.Remove(type);
    }

    /// <summary>A property's own record type, or a collection's element record type; null for scalars.</summary>
    private static Type? NestedRecord(Type type)
    {
        var element = type.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(type)
            ? type.GetGenericArguments()[^1]
            : type;
        element = Nullable.GetUnderlyingType(element) ?? element;

        return element.Namespace?.StartsWith("System", StringComparison.Ordinal) == true || element.IsPrimitive || element.IsEnum
            ? null
            : element.IsClass ? element : null;
    }
}
