using System.Security.Claims;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.jobs;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Sydowwe.Framework.application.service.auth;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.domain.serviceContract;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// P2-05 — <see cref="UnconfirmedAccountJobHandler"/>: a sign-up that never confirmed gets one reminder a day in, and
/// is deleted after two weeks. Both halves fail silently — a reminder too many is spam to someone who asked for an
/// account once, a purge that misses a table keeps a person's data with nobody able to sign in and delete it — so every
/// case asserts on what the mail sender was handed, on the mark on the user row, and, for the purge, on every table
/// <see cref="UserDataInventory"/> knows.
/// <para>
/// <b>The clock is pinned to March 2099, and sign-ups are back-dated from it.</b> Quartz runs this job every hour inside
/// every test host with the <i>real</i> clock, which sees a 2099 sign-up as not yet a day old and leaves it alone — the
/// mirror of the 2020 pin the morning-briefing tests use, because here it is the sign-up's age that must be out of a
/// real clock's reach.
/// </para>
/// </summary>
[Collection("Postgres")]
public class UnconfirmedAccountJobTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly DateTime SignedUpAt = new(2099, 3, 2, 10, 0, 0, DateTimeKind.Utc);

    private static DateTimeOffset After(TimeSpan elapsed) => new(SignedUpAt + elapsed, TimeSpan.Zero);

    // ---- test doubles -----------------------------------------------------------------------------

    /// <summary>The confirmation links the job sent, per user, with their tokens; every other mail is a no-op.</summary>
    private sealed class MailRecorder
    {
        public Mock<IUserEmailSenderService<User>> Sender { get; } = new();
        public List<(long UserId, string Token)> ConfirmationLinks { get; } = [];

        public MailRecorder()
        {
            Sender.Setup(s => s.SendConfirmationLinkAsync(It.IsAny<User>(), It.IsAny<string>()))
                .Callback<User, string>((user, token) => ConfirmationLinks.Add((user.Id, token)))
                .Returns(Task.CompletedTask);
        }

        public void FailSends() =>
            Sender.Setup(s => s.SendConfirmationLinkAsync(It.IsAny<User>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("SMTP is down"));
    }

    /// <summary>The real background scope has no user. See <c>RoutineSuggestedTodayJobTests</c> for why the fake user cannot stand in.</summary>
    private sealed class NoAmbientUserLoggedUserService : ILoggedUserService
    {
        public ClaimsPrincipal? LoggedUserPrincipal => null;
        public bool IsAuthenticated => false;
        public IEnumerable<string> GetRoles => [];
        public string GetEmail => throw new InvalidOperationException("No ambient user in a background job scope.");
        public long GetUserId => throw new InvalidOperationException("No ambient user in a background job scope.");
        public long? GetUserIdOrNull => null;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // ---- the reminder -----------------------------------------------------------------------------

    [Fact(DisplayName = "P2-05: one confirmation reminder a day after sign-up, marked, and never a second")]
    public async Task Reminder_IsSentOnceADayIn()
    {
        var userId = await SignUpAsync("remind-once@test.com");
        var mail = new MailRecorder();

        await RunJobAsync(After(TimeSpan.FromHours(23)), mail);
        mail.ConfirmationLinks.Should().BeEmpty("a day has not passed yet");
        (await ReadMarkAsync(userId)).Should().BeNull();

        await RunJobAsync(After(TimeSpan.FromHours(25)), mail);
        await RunJobAsync(After(TimeSpan.FromHours(26)), mail);
        await RunJobAsync(After(TimeSpan.FromDays(5)), mail);

        mail.ConfirmationLinks.Should().ContainSingle().Which.UserId.Should().Be(userId);
        (await ReadMarkAsync(userId)).Should().Be(After(TimeSpan.FromHours(25)).UtcDateTime);

        // The link is a working one: its token confirms the address.
        await using var scope = Fixture.UnauthenticatedFactory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        (await userManager.ConfirmEmailAsync(user!, mail.ConfirmationLinks[0].Token)).Succeeded
            .Should().BeTrue("the reminder carries a fresh confirmation token, not a dead one");
    }

    [Fact(DisplayName = "P2-05: a reminder that failed to send is left unmarked and goes out on the next run")]
    public async Task Reminder_ThatFailed_IsRetried()
    {
        var userId = await SignUpAsync("remind-retry@test.com");

        var down = new MailRecorder();
        down.FailSends();
        await RunJobAsync(After(TimeSpan.FromHours(25)), down);
        (await ReadMarkAsync(userId)).Should().BeNull("nothing went out, so nothing may say it did");

        var up = new MailRecorder();
        await RunJobAsync(After(TimeSpan.FromHours(26)), up);
        up.ConfirmationLinks.Should().ContainSingle().Which.UserId.Should().Be(userId);
        (await ReadMarkAsync(userId)).Should().Be(After(TimeSpan.FromHours(26)).UtcDateTime);
    }

    // ---- the purge --------------------------------------------------------------------------------

    /// <summary>
    /// Two accounts, because the two kinds of data cannot share one: a real sign-up carries the per-user defaults
    /// registration creates, and a bare account carries <see cref="UserDataInventory"/>'s row in every slice (whose
    /// fixed values would collide with those defaults). Both are checked against every user-scoped table in the model.
    /// </summary>
    [Fact(DisplayName = "P2-05: two weeks unconfirmed, the account, its defaults and everything it owns are deleted")]
    public async Task Purge_DeletesTheAccountAndItsData()
    {
        var registeredId = await SignUpAsync("purge-registered@test.com");
        var bareId = await CreateBareAccountAsync("purge-bare@test.com");
        await using (var db = CreateDbContext())
        {
            (await UserDataInventory.UserScopedRowsAsync(db, registeredId, CancellationToken))
                .Should().NotBeEmpty("registration creates per-user defaults, or this test proves nothing about them");
            await UserDataInventory.SeedEverySliceAsync(db, bareId, CancellationToken);
        }

        var mail = new MailRecorder();
        await RunJobAsync(After(TimeSpan.FromDays(14) - TimeSpan.FromHours(1)), mail);
        (await UserExistsAsync(registeredId)).Should().BeTrue("it is not two weeks old yet");
        (await UserExistsAsync(bareId)).Should().BeTrue("it is not two weeks old yet");

        await RunJobAsync(After(TimeSpan.FromDays(14) + TimeSpan.FromHours(1)), mail);

        (await UserExistsAsync(registeredId)).Should().BeFalse();
        (await UserExistsAsync(bareId)).Should().BeFalse();
        await using (var db = CreateDbContext())
        {
            await UserDataInventory.AssertNoUserScopedRowsLeftAsync(db, registeredId, CancellationToken);
            await UserDataInventory.AssertErasedAsync(db, bareId, CancellationToken);
        }

        mail.ConfirmationLinks.Select(l => l.UserId).Should().BeEquivalentTo([registeredId, bareId],
            "the run inside the fortnight still reminded each, once");
    }

    [Fact(DisplayName = "P2-05: an account already due for deletion is deleted, not reminded first")]
    public async Task Purge_DueAccount_IsNotRemindedFirst()
    {
        var userId = await SignUpAsync("purge-unreminded@test.com");

        var mail = new MailRecorder();
        await RunJobAsync(After(TimeSpan.FromDays(20)), mail);

        mail.ConfirmationLinks.Should().BeEmpty();
        (await UserExistsAsync(userId)).Should().BeFalse();
    }

    // ---- never touched ----------------------------------------------------------------------------

    [Fact(DisplayName = "P2-05: confirmed accounts, Google sign-ins and the root admin are never reminded or deleted")]
    public async Task ConfirmedGoogleAndRootAdmin_AreNeverTouched()
    {
        var confirmedId = await SignUpAsync("confirmed@test.com", confirmed: true);
        var googleId = await SignUpAsync("google@test.com", googleOAuthUserId: "google-sub-1");
        // AppDbContextFixture sets ROOT_ADMIN_EMAIL to this address; the seeder creates it confirmed, this is the
        // belt-and-braces case of one that is not.
        var rootAdminId = await SignUpAsync("root@test.com");

        var mail = new MailRecorder();
        await RunJobAsync(After(TimeSpan.FromHours(25)), mail);
        await RunJobAsync(After(TimeSpan.FromDays(30)), mail);

        mail.ConfirmationLinks.Should().BeEmpty();
        (await UserExistsAsync(confirmedId)).Should().BeTrue();
        (await UserExistsAsync(googleId)).Should().BeTrue();
        (await UserExistsAsync(rootAdminId)).Should().BeTrue();
        (await UserExistsAsync(FakeLoggedUserService.TestUserId)).Should().BeTrue();
    }

    // ---- helpers ----------------------------------------------------------------------------------

    /// <summary>
    /// A sign-up through the real <see cref="UserRegistrationFlow"/> — account, default role and the per-user defaults,
    /// as <c>auth/register</c> creates them — back-dated to <see cref="SignedUpAt"/>.
    /// </summary>
    private async Task<long> SignUpAsync(string email, bool confirmed = false, string? googleOAuthUserId = null)
    {
        await using (var scope = Fixture.UnauthenticatedFactory.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var user = new User
            {
                Email = email,
                EmailConfirmed = confirmed,
                GoogleOAuthUserId = googleOAuthUserId,
                Locale = AvailableLocales.En,
                Timezone = TimeZoneInfo.Utc
            };

            var result = await UserRegistrationFlow.RunAsync(
                sp.GetRequiredService<UserManager<User>>(), sp.GetRequiredService<DbContext>(),
                sp.GetRequiredService<IUserDefaultsService>(), user,
                googleOAuthUserId is null ? AppDbContextFixture.TestUserPassword : null, ct: CancellationToken);
            result.Failed.Should().BeFalse(result.ErrorMessage);
        }

        return await BackdateAsync(email);
    }

    /// <summary>An unconfirmed account with no per-user defaults, back-dated to <see cref="SignedUpAt"/>.</summary>
    private async Task<long> CreateBareAccountAsync(string email)
    {
        await using (var scope = Fixture.UnauthenticatedFactory.Services.CreateAsyncScope())
        {
            var user = new User { Email = email, Locale = AvailableLocales.En, Timezone = TimeZoneInfo.Utc };
            (await scope.ServiceProvider.GetRequiredService<UserManager<User>>().CreateAsync(user, AppDbContextFixture.TestUserPassword))
                .Succeeded.Should().BeTrue();
        }

        return await BackdateAsync(email);
    }

    private async Task<long> BackdateAsync(string email)
    {
        await using var db = (AppDbContext)CreateDbContext();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync(CancellationToken);
        await db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.CreatedTimestamp, SignedUpAt), CancellationToken);
        return userId;
    }

    private async Task<bool> UserExistsAsync(long userId)
    {
        await using var db = (AppDbContext)CreateDbContext();
        return await db.Users.AnyAsync(u => u.Id == userId, CancellationToken);
    }

    private async Task<DateTime?> ReadMarkAsync(long userId)
    {
        await using var db = (AppDbContext)CreateDbContext();
        return await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => u.ConfirmationReminderSentAt).SingleAsync(CancellationToken);
    }

    /// <summary>Runs the job the way the Scheduler dispatcher does — one scope per fire, no ambient user — at <paramref name="now"/>.</summary>
    private async Task RunJobAsync(DateTimeOffset now, MailRecorder mail)
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configureServices: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));

            services.RemoveAll<ILoggedUserService>();
            services.AddScoped<ILoggedUserService>(_ => new NoAmbientUserLoggedUserService());

            services.RemoveAll<IUserEmailSenderService<User>>();
            services.AddSingleton(mail.Sender.Object);
        });

        await using var scope = factory.Services.CreateAsyncScope();
        var handler = (IScheduledJobHandler)ActivatorUtilities.CreateInstance(scope.ServiceProvider, typeof(UnconfirmedAccountJobHandler));

        await handler.ExecuteAsync(new ScheduledJobContext
        {
            ScheduledFireTimeUtc = now.UtcDateTime,
            ActualFireTimeUtc = now.UtcDateTime,
            JobKey = handler.Key,
            CorrelationId = "unconfirmed-account-test",
            TriggerSource = TriggerSource.Manual
        }, CancellationToken);
    }
}
