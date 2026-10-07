using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;
using Sydowwe.Framework.infrastructure.persistence.converter;

namespace AdhdTimeOrganizer.TodoLists.infrastructure.persistence.configuration.todoList;

public class TodoListItemConfiguration : IEntityTypeConfiguration<TodoListItem>
{
    public void Configure(EntityTypeBuilder<TodoListItem> builder)
    {
        builder.BaseEntityConfigure();

        builder.BaseTodoListConfigure();

        builder.Property(t => t.SuggestedTime)
            .HasConversion(new NullableIntTimeConverter());

        builder.IsManyWithOneUser();
        builder.IsManyWithOneActivity();

        builder.HasOne(r => r.TaskPriority)
            .WithMany(t => t.TodoListColl)
            .HasForeignKey(r => r.TaskPriorityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(t => t.DueDate).IsRequired(false);
        builder.Property(t => t.DueTime).IsRequired(false);
        builder.Property(t => t.FirstDueDate).IsRequired(false);
        builder.Property(t => t.PostponeCount).IsRequired();

        // Temptation bundling. Declared without a navigation on either end: nothing reads through it
        // (responses carry the bare id), and a second navigation to Activity would sit beside the
        // owning one from IsManyWithOneActivity for no gain.
        // SetNull, not Cascade: deleting the paired leisure activity unpairs the task, it does not
        // delete it — unlike the owning activity, which does cascade the item away.
        builder.HasOne<Activity>()
            .WithMany()
            .HasForeignKey(t => t.PairedLeisureActivityId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // Postgres does not index a FK for you, and the SetNull above has to find these rows.
        builder.HasIndex(t => t.PairedLeisureActivityId);

        builder.HasIndex(t => new { t.UserId, t.ActivityId, t.TodoListId })
            .IsUnique();

        // DisplayOrder is part of the key, not a tie-breaker: GetNextDisplayOrder takes MIN over the
        // group and the rebalance walks it ORDER BY display_order DESC. Filter columns alone would
        // leave both as sorts.
        builder.HasIndex(t => new { t.UserId, t.TaskPriorityId, t.DisplayOrder });

        // GetAllTodoListItemEndpoint filters by list and orders by DisplayOrder; TodoListResponse's
        // two count subqueries probe the same leading columns.
        builder.HasIndex(t => new { t.UserId, t.TodoListId, t.DisplayOrder });

        // The dashboard widget's only query. Partial, because it never reads done items — which are
        // the majority of the table over time.
        builder.HasIndex(t => new { t.UserId, t.IsDone, t.DueDate })
            .HasFilter("NOT is_done");

        // The daily recap's only query: this user's items completed within one day. Without it the
        // scan is the user's whole item set, filtered on a column that is null for everything still
        // open.
        builder.HasIndex(t => new { t.UserId, t.CompletedTimestamp });
    }
}