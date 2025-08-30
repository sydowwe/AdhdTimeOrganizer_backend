using AdhdTimeOrganizer.application.commands;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.result;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.handler.command.user;

public class CreateDefaultsForNewUserCommandHandler(AppCommandDbContext dbContext, ILogger<CreateDefaultsForNewUserCommandHandler> logger) : ICommandHandler<CreateDefaultsForNewUserCommand, Result>
{
    public async Task<Result> ExecuteAsync(CreateDefaultsForNewUserCommand command, CancellationToken ct)
    {
        await CreateTaskUrgencies(command.UserId, ct);
        await CreateRoutineTimePeriod(command.UserId, ct);
        await CreateRoles(command.UserId, ct);

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to create defaults for new user");
            return DbUtils.HandleException(e, nameof(CreateDefaultsForNewUserCommandHandler));
        }

        return Result.Successful();
    }

    private async Task CreateTaskUrgencies(long userId, CancellationToken ct = default)
    {
        await dbContext.TaskUrgencies.AddRangeAsync(
            [
                new TaskUrgency { UserId = userId, Text = "Today", Color = "#FF5252", Priority = 1 }, // Red
                new TaskUrgency { UserId = userId, Text = "This week", Color = "#FFA726", Priority = 2 }, // Orange
                new TaskUrgency { UserId = userId, Text = "This month", Color = "#FFD600", Priority = 3 }, // Yellow
                new TaskUrgency { UserId = userId, Text = "This year", Color = "#4CAF50", Priority = 4 }
            ], ct
        );
    }

    private async Task CreateRoutineTimePeriod(long userId, CancellationToken ct = default)
    {
        await dbContext.RoutineTimePeriods.AddRangeAsync(
            [
                new RoutineTimePeriod { UserId = userId, Text = "Daily", Color = "#92F58C", LengthInDays = 1 }, // Green
                new RoutineTimePeriod { UserId = userId, Text = "Weekly", Color = "#936AF1", LengthInDays = 7 }, // purple
                new RoutineTimePeriod { UserId = userId, Text = "Monthly", Color = "#2C7EF4", LengthInDays = 30 }, // blue
                new RoutineTimePeriod { UserId = userId, Text = "Yearly", Color = "#A5CCF3", LengthInDays = 365 }
            ], ct
        );
    }

    private async Task CreateRoles(long userId, CancellationToken ct = default)
    {
        await dbContext.ActivityRoles.AddRangeAsync(
            [
                new ActivityRole { UserId = userId, Name = "Planner task", Text = "Quickly created activities in task planner", Color = "", Icon = "calendar-days" },
                new ActivityRole { UserId = userId, Name = "To-do list task", Text = "Quickly created activities in to-do list", Color = "", Icon = "list-check" },
                new ActivityRole { UserId = userId, Name = "Routine task", Text = "Quickly created activities in routine to-do list", Color = "", Icon = "recycle" }
            ], ct
        );
    }
}