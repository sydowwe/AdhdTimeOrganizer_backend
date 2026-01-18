using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.dev;

public class ActivitySeeder(
    AppCommandDbContext dbContext,
    ILogger<ActivitySeeder> logger) : IDevDatabaseSeeder, IScopedService
{
    public string SeederName => "Activity";
    public int Order => 9;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableAsync<Activity>();
    }

    public async Task SeedForUser(long userId)
    {
        // Note: TruncateTable already removed existing data for this user

        // Get roles and categories for this user
        var roles = await dbContext.ActivityRoles
            .Where(ar => ar.UserId == userId)
            .ToListAsync();

        var categories = await dbContext.ActivityCategories
            .Where(ac => ac.UserId == userId)
            .ToListAsync();

        if (!roles.Any())
        {
            logger.LogWarning("No activity roles found for user {UserId}. Skipping activity seeding.", userId);
            return;
        }

        // Helper to find role by name
        var workRole = roles.FirstOrDefault(r => r.Name == "Work");
        var healthRole = roles.FirstOrDefault(r => r.Name == "Health & Fitness");
        var personalDevRole = roles.FirstOrDefault(r => r.Name == "Personal Development");
        var socialRole = roles.FirstOrDefault(r => r.Name == "Social");
        var householdRole = roles.FirstOrDefault(r => r.Name == "Household");
        var selfCareRole = roles.FirstOrDefault(r => r.Name == "Self-Care");
        var hobbiesRole = roles.FirstOrDefault(r => r.Name == "Hobbies & Leisure");

        // Helper to find category by name
        var deepWorkCat = categories.FirstOrDefault(c => c.Name == "Deep Work");
        var quickTasksCat = categories.FirstOrDefault(c => c.Name == "Quick Tasks");
        var urgentImportantCat = categories.FirstOrDefault(c => c.Name == "Urgent & Important");
        var importantNotUrgentCat = categories.FirstOrDefault(c => c.Name == "Important Not Urgent");
        var learningCat = categories.FirstOrDefault(c => c.Name == "Learning");

        var activities = new List<Activity>();

        // Work activities
        if (workRole != null)
        {
            activities.AddRange([
                new Activity
                {
                    Name = "Daily Standup",
                    Text = "Team sync meeting every morning",
                    IsUnavoidable = true,
                    RoleId = workRole.Id,
                    CategoryId = quickTasksCat?.Id,
                    UserId = userId
                },
                new Activity
                {
                    Name = "Code Review",
                    Text = "Review pull requests from team members",
                    IsUnavoidable = false,
                    RoleId = workRole.Id,
                    CategoryId = importantNotUrgentCat?.Id,
                    UserId = userId
                },
                new Activity
                {
                    Name = "Sprint Planning",
                    Text = "Plan tasks for the upcoming sprint",
                    IsUnavoidable = true,
                    RoleId = workRole.Id,
                    CategoryId = urgentImportantCat?.Id,
                    UserId = userId
                },
                new Activity
                {
                    Name = "Feature Development",
                    Text = "Work on new features for the project",
                    IsUnavoidable = false,
                    RoleId = workRole.Id,
                    CategoryId = deepWorkCat?.Id,
                    UserId = userId
                },
                new Activity
                {
                    Name = "Bug Fixing",
                    Text = "Address reported bugs and issues",
                    IsUnavoidable = false,
                    RoleId = workRole.Id,
                    CategoryId = urgentImportantCat?.Id,
                    UserId = userId
                }
            ]);
        }

        // Health & Fitness activities
        if (healthRole != null)
        {
            activities.AddRange([
                new Activity
                {
                    Name = "Morning Exercise",
                    Text = "Start the day with physical activity",
                    IsUnavoidable = false,
                    RoleId = healthRole.Id,
                    CategoryId = importantNotUrgentCat?.Id,
                    UserId = userId
                },
                new Activity
                {
                    Name = "Meal Preparation",
                    Text = "Prepare healthy meals for the day",
                    IsUnavoidable = true,
                    RoleId = healthRole.Id,
                    CategoryId = null,
                    UserId = userId
                },
                new Activity
                {
                    Name = "Doctor Appointment",
                    Text = "Regular health checkup",
                    IsUnavoidable = true,
                    RoleId = healthRole.Id,
                    CategoryId = urgentImportantCat?.Id,
                    UserId = userId
                }
            ]);
        }

        // Personal Development activities
        if (personalDevRole != null)
        {
            activities.AddRange([
                new Activity
                {
                    Name = "Online Course",
                    Text = "Complete modules from online learning platform",
                    IsUnavoidable = false,
                    RoleId = personalDevRole.Id,
                    CategoryId = learningCat?.Id,
                    UserId = userId
                },
                new Activity
                {
                    Name = "Read Technical Book",
                    Text = "Read chapters from programming books",
                    IsUnavoidable = false,
                    RoleId = personalDevRole.Id,
                    CategoryId = learningCat?.Id,
                    UserId = userId
                },
                new Activity
                {
                    Name = "Practice Coding",
                    Text = "Solve coding challenges and practice algorithms",
                    IsUnavoidable = false,
                    RoleId = personalDevRole.Id,
                    CategoryId = deepWorkCat?.Id,
                    UserId = userId
                }
            ]);
        }

        // Social activities
        if (socialRole != null)
        {
            activities.AddRange([
                new Activity
                {
                    Name = "Family Dinner",
                    Text = "Evening meal with family",
                    IsUnavoidable = true,
                    RoleId = socialRole.Id,
                    CategoryId = null,
                    UserId = userId
                },
                new Activity
                {
                    Name = "Coffee with Friends",
                    Text = "Catch up with friends over coffee",
                    IsUnavoidable = false,
                    RoleId = socialRole.Id,
                    CategoryId = null,
                    UserId = userId
                },
                new Activity
                {
                    Name = "Video Call Parents",
                    Text = "Weekly video call with parents",
                    IsUnavoidable = false,
                    RoleId = socialRole.Id,
                    CategoryId = importantNotUrgentCat?.Id,
                    UserId = userId
                }
            ]);
        }

        // Household activities
        if (householdRole != null)
        {
            activities.AddRange([
                new Activity
                {
                    Name = "Grocery Shopping",
                    Text = "Weekly grocery shopping trip",
                    IsUnavoidable = true,
                    RoleId = householdRole.Id,
                    CategoryId = null,
                    UserId = userId
                },
                new Activity
                {
                    Name = "Laundry",
                    Text = "Wash and fold clothes",
                    IsUnavoidable = true,
                    RoleId = householdRole.Id,
                    CategoryId = quickTasksCat?.Id,
                    UserId = userId
                },
                new Activity
                {
                    Name = "House Cleaning",
                    Text = "Clean and organize living space",
                    IsUnavoidable = false,
                    RoleId = householdRole.Id,
                    CategoryId = null,
                    UserId = userId
                }
            ]);
        }

        // Self-Care activities
        if (selfCareRole != null)
        {
            activities.AddRange([
                new Activity
                {
                    Name = "Meditation",
                    Text = "Daily mindfulness meditation session",
                    IsUnavoidable = false,
                    RoleId = selfCareRole.Id,
                    CategoryId = importantNotUrgentCat?.Id,
                    UserId = userId
                },
                new Activity
                {
                    Name = "Journaling",
                    Text = "Reflect and write in personal journal",
                    IsUnavoidable = false,
                    RoleId = selfCareRole.Id,
                    CategoryId = null,
                    UserId = userId
                },
                new Activity
                {
                    Name = "Sleep Routine",
                    Text = "Wind down and prepare for sleep",
                    IsUnavoidable = true,
                    RoleId = selfCareRole.Id,
                    CategoryId = null,
                    UserId = userId
                }
            ]);
        }

        // Hobbies & Leisure activities
        if (hobbiesRole != null)
        {
            activities.AddRange([
                new Activity
                {
                    Name = "Gaming Session",
                    Text = "Play video games for relaxation",
                    IsUnavoidable = false,
                    RoleId = hobbiesRole.Id,
                    CategoryId = null,
                    UserId = userId
                },
                new Activity
                {
                    Name = "Watch Movie/Series",
                    Text = "Entertainment time",
                    IsUnavoidable = false,
                    RoleId = hobbiesRole.Id,
                    CategoryId = null,
                    UserId = userId
                },
                new Activity
                {
                    Name = "Side Project",
                    Text = "Work on personal programming project",
                    IsUnavoidable = false,
                    RoleId = hobbiesRole.Id,
                    CategoryId = deepWorkCat?.Id,
                    UserId = userId
                }
            ]);
        }

        await dbContext.Activities.AddRangeAsync(activities);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} activities for user {UserId}", activities.Count, userId);
    }
}
