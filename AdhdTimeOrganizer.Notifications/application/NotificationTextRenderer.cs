using System.Text.Json;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Notifications.application;

/// <summary>
/// v1 renderer with sk-SK defaults. Stateless — registered as a singleton. To localize per
/// recipient, replace with an implementation that takes the user's culture and resolves
/// strings from resource files.
/// <para>
/// Each branch deserializes the <c>Sydowwe.Framework.Contracts</c> payload record its <see cref="NotificationType"/> declares, so the
/// producer's shape and the text that reads it are coupled at compile time. One rule holds everywhere:
/// <b>every branch keeps a degraded fallback.</b> A payload can be empty, malformed, or written by an older
/// shape, and it must still render rather than throw.
/// </para>
/// </summary>
public class NotificationTextRenderer : INotificationTextRenderer, ISingletonService
{
    public (string Title, string Body) Render(NotificationType type, string payloadJson)
    {
        return type switch
        {
            NotificationType.DeadlineApproaching => (
                "Blíži sa termín",
                Payload<DeadlineApproachingPayload>(payloadJson)?.Title is { Length: > 0 } title
                    ? $"Blíži sa termín: {title}."
                    : "Blíži sa termín."),

            NotificationType.ReminderDigest => (
                "Pripomienky",
                Payload<ReminderDigestPayload>(payloadJson) is { Count: > 0 } digest
                    ? $"Máte {digest.Count} naplánovaných pripomienok."
                    : "Máte naplánované pripomienky."),

            // The user wrote this title themselves, so it is the whole point of the notification — but it
            // can be absent (a shape-drifted or empty document), hence the name-less fallback.
            NotificationType.PersonalReminder => (
                "Pripomienka",
                Payload<PersonalReminderPayload>(payloadJson)?.Title is { Length: > 0 } reminderTitle
                    ? reminderTitle
                    : "Máte pripomienku."),

            NotificationType.RoutinePeriodEndingSoon => (
                "Obdobie sa čoskoro končí",
                RenderPeriodEndingSoonBody(Payload<RoutinePeriodEndingSoonPayload>(payloadJson))),

            NotificationType.RoutinePeriodEnded => (
                "Obdobie sa skončilo",
                RenderPeriodEndedBody(Payload<RoutinePeriodEndedPayload>(payloadJson))),

            NotificationType.RoutineStreakGraceExpiring => (
                "Séria je ohrozená",
                RenderGraceExpiringBody(Payload<RoutineStreakGraceExpiringPayload>(payloadJson))),

            NotificationType.Test => (
                "Testovacia notifikácia",
                Payload<TestNotificationPayload>(payloadJson)?.Message is { Length: > 0 } msg
                    ? msg
                    : "Toto je testovacia notifikácia."),

            NotificationType.ScheduledJobFailed => (
                "Naplánovaná úloha zlyhala",
                // jobKey is a technical identifier (not PII) — safe to show; it deep-links the admin to the run.
                Payload<ScheduledJobFailedPayload>(payloadJson)?.JobKey is { Length: > 0 } failedJobKey
                    ? $"Naplánovaná úloha „{failedJobKey}“ zlyhala a vyžaduje pozornosť."
                    : "Naplánovaná úloha zlyhala a vyžaduje pozornosť."),

            NotificationType.ScheduledJobOverdue => (
                "Naplánovaná úloha sa nespustila",
                RenderJobOverdueBody(Payload<ScheduledJobOverduePayload>(payloadJson))),

            _ => ("Nová notifikácia", "Máte novú notifikáciu.")
        };
    }

    /// <summary>
    /// The overdue body (scheduler follow-up 08). Deliberately worded as <i>silence</i>, not an error — the
    /// admin's next step is to check the scheduler/registration, not the handler's code.
    /// <para>
    /// Unlike the failed-job body there is no run id to deep-link (an overdue job has no run row by
    /// definition), so the job key is the only handle; the missed fire time is added when known, because "since
    /// when" is what tells an admin whether this is a blip or a dead scheduler. Both are technical, non-PII.
    /// </para>
    /// </summary>
    private static string RenderJobOverdueBody(ScheduledJobOverduePayload? payload)
    {
        var jobKey = payload?.JobKey is { Length: > 0 } key ? $"„{key}“" : null;
        var expected = payload?.ExpectedRunAt is { } at ? $" Očakávané spustenie: {at:dd.MM.yyyy HH:mm} UTC." : string.Empty;

        return jobKey is null
            ? $"Naplánovaná úloha sa nespustila v očakávanom čase a vyžaduje pozornosť.{expected}"
            : $"Naplánovaná úloha {jobKey} sa nespustila v očakávanom čase a vyžaduje pozornosť.{expected}";
    }

    /// <summary>
    /// The lead-time nudge body. Leads with what is still outstanding — the number the user acts on — with the
    /// progress and streak added only when there is something to say. Null payload keeps the degraded fallback.
    /// </summary>
    private static string RenderPeriodEndingSoonBody(RoutinePeriodEndingSoonPayload? payload)
    {
        if (payload is null)
            return "Obdobie sa čoskoro končí a máte nedokončené úlohy.";

        var name = payload.PeriodName is { Length: > 0 } periodName ? $"„{periodName}“" : "Obdobie";
        var progress = payload.Total > 0 ? $" Hotové {payload.Total - payload.Remaining} z {payload.Total}." : string.Empty;
        var streak = payload.Streak > 0 ? $" Séria: {payload.Streak}." : string.Empty;

        return $"{name} sa končí o {payload.DaysLeft} dní, zostáva {payload.Remaining} úloh.{progress}{streak}";
    }

    /// <summary>
    /// The post-reset summary. The streak outcome picks the closing sentence; <c>Unknown</c> is the shape-drift
    /// fallback and says nothing about the streak rather than guessing at it.
    /// </summary>
    private static string RenderPeriodEndedBody(RoutinePeriodEndedPayload? payload)
    {
        if (payload is null)
            return "Obdobie sa skončilo.";

        var name = payload.PeriodName is { Length: > 0 } periodName ? $"„{periodName}“" : "Obdobie";
        var outcome = payload.Outcome switch
        {
            RoutineStreakOutcome.Extended => $" Séria pokračuje: {payload.Streak}.",
            RoutineStreakOutcome.OnGrace => $" Séria ({payload.Streak}) je zatiaľ zachovaná v odkladovej lehote.",
            RoutineStreakOutcome.Broken => " Séria bola prerušená.",
            _ => string.Empty
        };

        return $"{name} sa skončilo: hotové {payload.CompletedCount} z {payload.TotalCount}.{outcome}";
    }

    /// <summary>
    /// The grace-window warning. Fires mid-period, so it names what is at stake and by when — the deadline is
    /// the actionable half.
    /// </summary>
    private static string RenderGraceExpiringBody(RoutineStreakGraceExpiringPayload? payload)
    {
        if (payload is null)
            return "Odkladová lehota pre vašu sériu sa čoskoro končí.";

        var name = payload.PeriodName is { Length: > 0 } periodName ? $"„{periodName}“" : "obdobie";
        var until = payload.GraceUntil is { } graceUntil ? $" do {graceUntil:dd.MM.yyyy HH:mm}" : string.Empty;

        return $"Séria {payload.Streak} pre {name} sa preruší, ak ju nedokončíte{until}.";
    }

    /// <summary>
    /// Deserializes the payload record for one notification kind. Returns null — never throws — for an empty,
    /// malformed or shape-drifted document, so every caller's name-less fallback branch stays reachable.
    /// </summary>
    private static T? Payload<T>(string payloadJson) where T : class
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;
        try
        {
            return JsonHelper.Deserialize<T>(payloadJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}