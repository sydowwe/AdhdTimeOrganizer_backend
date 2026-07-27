using AdhdTimeOrganizer.Reminders.domain.entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Reminders.infrastructure.persistence.configuration;

public class ReminderOccurrenceActionEntityConfiguration : IEntityTypeConfiguration<ReminderOccurrenceAction>
{
    public void Configure(EntityTypeBuilder<ReminderOccurrenceAction> b)
    {
        b.BaseEntityConfigure();

        b.EnumColumn(x => x.ActionType);

        // Restrict: the action log is an append-only ledger — never cascade-delete it with its definition.
        b.HasOne(x => x.ReminderDefinition)
            .WithMany()
            .HasForeignKey(x => x.ReminderDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Reversal lineage — a correction row points at the action it reverses.
        b.HasOne(x => x.ReversesAction)
            .WithMany()
            .HasForeignKey(x => x.ReversesActionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Resolve-recipients drop lookup: the effective action for (definition, occurrence, user).
        b.HasIndex(x => new { x.ReminderDefinitionId, x.OccurrenceAt, x.UserId });

        // The second "due snoozes" source: snooze markers whose SnoozeUntil has passed.
        b.HasIndex(x => new { x.ActionType, x.SnoozeUntil });
    }
}