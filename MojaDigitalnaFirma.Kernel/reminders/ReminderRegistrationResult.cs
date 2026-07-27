namespace MojaDigitalnaFirma.Kernel.reminders;

/// <summary>
/// The outcome of <see cref="IReminderRegistry.RegisterAsync"/>: the id of the upserted definition, the
/// <see cref="ReminderKey"/> it was keyed by, and whether the call inserted a new definition or updated an
/// existing one in place. Lets callers log/assert the upsert outcome.
/// </summary>
/// <param name="DefinitionId">The id of the reminder definition row (new or existing).</param>
/// <param name="Key">The idempotency key the registration was keyed by.</param>
/// <param name="Created">True if a new definition was inserted; false if an existing one was updated in place.</param>
public sealed record ReminderRegistrationResult(long DefinitionId, ReminderKey Key, bool Created);