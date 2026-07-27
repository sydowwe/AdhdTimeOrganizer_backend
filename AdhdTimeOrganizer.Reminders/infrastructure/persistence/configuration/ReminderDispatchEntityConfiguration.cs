using AdhdTimeOrganizer.Reminders.domain.entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Reminders.infrastructure.persistence.configuration;

public class ReminderDispatchEntityConfiguration : IEntityTypeConfiguration<ReminderDispatch>
{
    public void Configure(EntityTypeBuilder<ReminderDispatch> b)
    {
        b.BaseEntityConfigure();

        b.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
        b.Property(x => x.TemplateKeySnapshot).HasMaxLength(200);
        b.Property(x => x.RecipientsSnapshot).HasColumnType("jsonb").IsRequired();

        b.EnumColumn(x => x.Outcome);
        b.EnumColumn(x => x.SkipReason);
        b.EnumColumn(x => x.NotificationTypeSnapshot);

        // Restrict: the dispatch log is an append-only ledger — never cascade-delete history with its
        // definition.
        b.HasOne(x => x.ReminderDefinition)
            .WithMany()
            .HasForeignKey(x => x.ReminderDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Reversal lineage — a correction row points at the dispatch it reverses.
        b.HasOne(x => x.ReversesDispatch)
            .WithMany()
            .HasForeignKey(x => x.ReversesDispatchId)
            .OnDelete(DeleteBehavior.Restrict);

        // Snooze fulfilment (phase 05b) — a snoozed re-delivery points at the action it delivered. Restrict:
        // the action ledger is append-only too.
        b.HasOne(x => x.ReminderOccurrenceAction)
            .WithMany()
            .HasForeignKey(x => x.ReminderOccurrenceActionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Non-unique — powers the scanner's "already dispatched this occurrence?" dedup check (phase 03).
        b.HasIndex(x => new { x.ReminderDefinitionId, x.OccurrenceAt });

        // Marker-state dedup (phase 05b): "has this snooze already been delivered?"
        b.HasIndex(x => x.ReminderOccurrenceActionId);

        // Reversal-lineage lookup (PERF-2): powers `LoadEffectiveOccurrencesAsync`'s per-scan
        // "is this dispatch reversed?" subquery and the overview's effective-failed exclusion. Without it
        // both run unindexed scans that scale with the append-only ledger.
        b.HasIndex(x => x.ReversesDispatchId);

        // Partial index over failed rows only (PERF-1 / PERF-3): powers the overview's
        // `FailedNeedingAttention` roll-up and the dispatch-history "Failed" filter without scanning the
        // whole append-only ledger. Stays tiny because failures are the rare case. (Outcome is stored as a
        // string via EnumColumn, so the filter compares against the literal enum name.)
        b.HasIndex(x => x.Outcome)
            .HasFilter("outcome = 'Failed'");
    }
}