namespace MojaDigitalnaFirma.Kernel.reminders;

/// <summary>
/// How a reminder's recipients are determined (see <see cref="ReminderRegistration"/>). Either fixed
/// at registration time, or resolved at dispatch time by an owner-supplied strategy.
/// </summary>
public enum RecipientMode
{
    /// <summary>Recipients are an explicit set of user ids supplied at registration time.</summary>
    ExplicitUsers,

    /// <summary>
    /// Recipients are resolved at dispatch time via an <see cref="IReminderRecipientResolver"/> whose
    /// <c>Key</c> matches the registration's resolver key (e.g. "the subject's manager").
    /// </summary>
    ResolverStrategy
}