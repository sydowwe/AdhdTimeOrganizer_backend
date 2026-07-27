namespace MojaDigitalnaFirma.Kernel.reminders;

/// <summary>
/// Keyed strategy an <b>owning module</b> implements to resolve a reminder's recipients at dispatch time
/// (e.g. "the subject's manager"). The Reminders module selects the implementation whose <see cref="Key"/>
/// matches the registration's <c>RecipientResolverKey</c> and calls <see cref="ResolveAsync"/> during the
/// scan (phase 03); it never encodes the domain rule itself.
/// <para>
/// Runs in a background context with no authenticated user — it must resolve its own scope and never
/// assume a logged-in principal.
/// </para>
/// </summary>
public interface IReminderRecipientResolver
{
    /// <summary>Stable key matched against <see cref="ReminderRegistration.RecipientResolverKey"/>.</summary>
    string Key { get; }

    /// <summary>Resolve the recipient user ids for the given occurrence.</summary>
    Task<IReadOnlyCollection<long>> ResolveAsync(ReminderResolutionContext context, CancellationToken ct);
}