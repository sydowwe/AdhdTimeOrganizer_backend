using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sydowwe.Framework.application.dto.response.user;
using Sydowwe.Framework.config;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.extService.user;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Auth;

/// <summary>
/// P1-05: every change to a password, the login address or 2FA mails the account owner, and nothing about that
/// mail can fail the change.
/// <para>
/// The real <see cref="UserEmailSenderService{TUser}"/> runs end to end, stopped only at the SMTP hop, so these
/// pin what silently breaks: an endpoint base that stops calling the alert service, a mail that goes to the
/// wrong address (the new one, after an email change, is the attacker's), and an undo link that no longer
/// undoes. Each test signs in an account of its own, because several of these revoke every session.
/// </para>
/// </summary>
[Collection("Postgres")]
public class SecurityAlertTests(AppDbContextFixture fixture) : AuthTestBase(fixture)
{
    private const string NewPassword = "NewTest@5678!";

    /// <summary>The SPA origin every mailed link is built on — whatever the host's <c>.env</c> says.</summary>
    private static string PageUrl => Helper.GetEnvVar("PAGE_URL");

    [Fact]
    public async Task ChangePassword_MailsTheOwner()
    {
        var (userId, email) = await CreateRealUserAsync(Fixture, "alert-pwchange", TestPassword);
        var mail = new CapturingUserEmailSender();
        await using var factory = CreateFactory(userId, mail);

        (await factory.CreateClient().PatchAsJsonAsync("api/user/password",
                new { Password = TestPassword, NewPassword }, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var sent = mail.Sent.Should().ContainSingle().Subject;
        sent.To.Should().Be(email);
        sent.Subject.Should().Contain("Your password was changed");
        sent.Html.Should().Contain("(UTC)", "the time is given in the user's own zone, and says which one");
        sent.Html.Should().Contain($"{PageUrl}/forgotten-password", "the way out if it wasn't them");
    }

    [Fact]
    public async Task ResetPassword_MailsTheOwner()
    {
        var (userId, email) = await CreateRealUserAsync(Fixture, "alert-pwreset", TestPassword);
        var mail = new CapturingUserEmailSender();
        await using var factory = CreateFactory(userId, mail);

        string token;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            token = await userManager.GeneratePasswordResetTokenAsync((await userManager.FindByIdAsync(userId.ToString()))!);
        }

        (await factory.CreateClient().PostAsJsonAsync("api/auth/reset-password",
                new { UserId = userId, Token = token, NewPassword, RecaptchaToken = "test" }, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var sent = mail.Sent.Should().ContainSingle().Subject;
        sent.To.Should().Be(email);
        sent.Subject.Should().Contain("Your password was reset");
    }

    /// <summary>
    /// The alert goes to the address the account had, shows the one it has now, and carries a link that takes it
    /// back. Following that link is itself an email change, so the address it was taken from gets an alert too.
    /// </summary>
    [Fact]
    public async Task ConfirmEmailChange_MailsThePreviousAddress_WithALinkThatTakesItBack()
    {
        var (userId, oldEmail) = await CreateRealUserAsync(Fixture, "alert-emailchange", TestPassword);
        var newEmail = $"alert-emailchange-new-{Guid.NewGuid():N}@integration.com";
        var mail = new CapturingUserEmailSender();
        await using var factory = CreateFactory(userId, mail);
        var client = factory.CreateClient();

        string token;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            token = await userManager.GenerateChangeEmailTokenAsync((await userManager.FindByIdAsync(userId.ToString()))!, newEmail);
        }

        (await client.PostAsJsonAsync("api/user/change-email/confirm",
                new { UserId = userId, NewEmail = newEmail, Token = token }, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var alert = mail.Sent.Should().ContainSingle().Subject;
        alert.To.Should().Be(oldEmail, "the owner reads the old mailbox; the new one may be the attacker's");
        alert.Subject.Should().Contain("Your sign-in email was changed");
        alert.Html.Should().Contain(newEmail, "the owner needs to see where the account went");
        alert.Html.Should().NotContain("/forgotten-password",
            "a reset link would now go to the new address, so the mail must not offer it as the way out");

        var undo = EmailChangeLinkIn(alert.Html);
        undo.UserId.Should().Be(userId);
        undo.Email.Should().Be(oldEmail);

        (await client.PostAsJsonAsync("api/user/change-email/confirm",
                new { UserId = userId, NewEmail = undo.Email, undo.Token }, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent, "the undo link is a working change-email token");

        using (var scope = factory.Services.CreateScope())
        {
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByIdAsync(userId.ToString());
            user!.Email.Should().Be(oldEmail);
            user.UserName.Should().Be(oldEmail, "the login identifier follows the address back");
        }

        mail.Sent.Should().HaveCount(2);
        mail.Sent.Last().To.Should().Be(newEmail, "taking the address back is a change too, told to the address it left");
    }

    /// <summary>
    /// The first step of an email change used to mail the token to the <i>current</i> address, so the new one was
    /// attached without anyone proving they could read it.
    /// </summary>
    [Fact]
    public async Task ChangeEmail_MailsTheConfirmationLinkToTheNewAddress()
    {
        var (userId, _) = await CreateRealUserAsync(Fixture, "alert-emailrequest", TestPassword);
        var newEmail = $"alert-emailrequest-new-{Guid.NewGuid():N}@integration.com";
        var mail = new CapturingUserEmailSender();
        await using var factory = CreateFactory(userId, mail);

        (await factory.CreateClient().PatchAsJsonAsync("api/user/change-email",
                new { Password = TestPassword, NewEmail = newEmail }, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var sent = mail.Sent.Should().ContainSingle().Subject;
        sent.To.Should().Be(newEmail);
        EmailChangeLinkIn(sent.Html).Email.Should().Be(newEmail);
    }

    [Fact]
    public async Task ToggleTwoFactor_MailsTheOwner_BothOnAndOff()
    {
        var (userId, email) = await CreateRealUserAsync(Fixture, "alert-2fa-toggle", TestPassword);
        var mail = new CapturingUserEmailSender();
        await using var factory = CreateFactory(userId, mail);
        var client = factory.CreateClient();

        var on = await client.PostAsJsonAsync("api/user/2fa/toggle", new { Password = TestPassword }, CancellationToken);
        on.StatusCode.Should().Be(HttpStatusCode.OK);
        var setup = await on.Content.ReadFromJsonAsync<TwoFactorAuthResponse>(CancellationToken);
        setup!.TwoFactorEnabled.Should().BeTrue();

        (await client.PostAsJsonAsync("api/user/2fa/toggle",
                new { Password = TestPassword, TwoFactorAuthToken = AuthFunctionalTests.GenerateTotp(setup.SharedKey!, DateTimeOffset.UtcNow) },
                CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        mail.Sent.Select(m => m.To).Should().Equal(email, email);
        mail.Sent.Select(m => m.Subject).Should().SatisfyRespectively(
            s => s.Should().Contain("Two-factor authentication was turned on",
                "enrolling an authenticator is how someone holding only the password locks the owner out"),
            s => s.Should().Contain("Two-factor authentication was turned off"));
    }

    [Fact]
    public async Task ResetTwoFactor_MailsTheOwner()
    {
        var (userId, email) = await CreateRealUserAsync(Fixture, "alert-2fa-reset", TestPassword);
        var mail = new CapturingUserEmailSender();
        await using var factory = CreateFactory(userId, mail);
        var key = await EnableTwoFactorAsync(factory, userId);

        (await factory.CreateClient().PostAsJsonAsync("api/user/2fa/reset",
                new { Password = TestPassword, TwoFactorAuthToken = AuthFunctionalTests.GenerateTotp(key, DateTimeOffset.UtcNow) },
                CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var sent = mail.Sent.Should().ContainSingle().Subject;
        sent.To.Should().Be(email);
        sent.Subject.Should().Contain("Your authenticator app was reset");
    }

    [Fact]
    public async Task RegenerateRecoveryCodes_MailsTheOwner()
    {
        var (userId, email) = await CreateRealUserAsync(Fixture, "alert-2fa-codes", TestPassword);
        var mail = new CapturingUserEmailSender();
        await using var factory = CreateFactory(userId, mail);
        var key = await EnableTwoFactorAsync(factory, userId);

        (await factory.CreateClient().PostAsJsonAsync("api/user/2fa/recovery-codes/regenerate",
                new { Password = TestPassword, TwoFactorAuthToken = AuthFunctionalTests.GenerateTotp(key, DateTimeOffset.UtcNow) },
                CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var sent = mail.Sent.Should().ContainSingle().Subject;
        sent.To.Should().Be(email);
        sent.Subject.Should().Contain("New recovery codes were generated");
    }

    // ── What the mail must never do ───────────────────────────────────────────

    [Fact]
    public async Task ChangePassword_StillSucceeds_WhenTheMailCannotBeSent()
    {
        var (userId, _) = await CreateRealUserAsync(Fixture, "alert-pwchange-smtpdown", TestPassword);
        var mail = new CapturingUserEmailSender { Fail = true };
        await using var factory = CreateFactory(userId, mail);

        (await factory.CreateClient().PatchAsJsonAsync("api/user/password",
                new { Password = TestPassword, NewPassword }, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        mail.Attempts.Should().Be(1, "it did try");
        await PasswordShouldBeAsync(factory, userId, NewPassword);
    }

    /// <summary>The email change has the most steps after the commit — the revert token among them.</summary>
    [Fact]
    public async Task ConfirmEmailChange_StillSucceeds_WhenTheMailCannotBeSent()
    {
        var (userId, _) = await CreateRealUserAsync(Fixture, "alert-emailchange-smtpdown", TestPassword);
        var newEmail = $"alert-emailchange-smtpdown-new-{Guid.NewGuid():N}@integration.com";
        var mail = new CapturingUserEmailSender { Fail = true };
        await using var factory = CreateFactory(userId, mail);

        string token;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            token = await userManager.GenerateChangeEmailTokenAsync((await userManager.FindByIdAsync(userId.ToString()))!, newEmail);
        }

        (await factory.CreateClient().PostAsJsonAsync("api/user/change-email/confirm",
                new { UserId = userId, NewEmail = newEmail, Token = token }, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        mail.Attempts.Should().Be(1);
        using var verify = factory.Services.CreateScope();
        (await verify.ServiceProvider.GetRequiredService<UserManager<User>>().FindByIdAsync(userId.ToString()))!
            .Email.Should().Be(newEmail);
    }

    [Fact]
    public async Task HostOptOut_SendsNothing_AndTheChangeStillHappens()
    {
        var (userId, _) = await CreateRealUserAsync(Fixture, "alert-optout", TestPassword);
        var mail = new CapturingUserEmailSender();
        await using var factory = CreateFactory(userId, mail,
            [new($"{SecurityAlertOptions.SectionName}:{nameof(SecurityAlertOptions.Enabled)}", "false")]);

        (await factory.CreateClient().PatchAsJsonAsync("api/user/password",
                new { Password = TestPassword, NewPassword }, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        mail.Attempts.Should().Be(0);
        await PasswordShouldBeAsync(factory, userId, NewPassword);
    }

    // ── Plumbing ──────────────────────────────────────────────────────────────

    /// <summary>Authenticated as <paramref name="userId"/> by the test handler, so the step-up check runs against that account's password.</summary>
    private ITestClientFactory CreateFactory(long userId, CapturingUserEmailSender mail,
        IEnumerable<KeyValuePair<string, string?>>? configOverrides = null) =>
        CreateFactory(TestRoles.User, userId, services =>
        {
            services.RemoveAll<IUserEmailSenderService<User>>();
            services.AddSingleton<IUserEmailSenderService<User>>(mail);
        }, configOverrides);

    /// <returns>The authenticator key, to compute codes from.</returns>
    private static async Task<string> EnableTwoFactorAsync(ITestClientFactory factory, long userId)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = (await userManager.FindByIdAsync(userId.ToString()))!;
        (await userManager.ResetAuthenticatorKeyAsync(user)).Succeeded.Should().BeTrue();
        (await userManager.SetTwoFactorEnabledAsync(user, true)).Succeeded.Should().BeTrue();
        return (await userManager.GetAuthenticatorKeyAsync(user))!;
    }

    private static async Task PasswordShouldBeAsync(ITestClientFactory factory, long userId, string password)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        (await userManager.CheckPasswordAsync(user!, password)).Should().BeTrue("the change itself went through");
    }

    private static (long UserId, string Email, string Token) EmailChangeLinkIn(string html)
    {
        var match = Regex.Match(html, $"href=\"{Regex.Escape(PageUrl)}/confirm-email-change\\?([^\"]+)\"");
        match.Success.Should().BeTrue("the mail links the confirm-email-change page");
        var query = QueryHelpers.ParseQuery(match.Groups[1].Value);
        return (long.Parse(query["userId"]!), query["email"]!, query["token"]!);
    }

    private sealed record SentMail(string To, string Subject, string Html);

    /// <summary>
    /// The real sender — templates, addressing, links — stopped at the SMTP hop. <see cref="Fail"/> makes that hop
    /// throw, as an SMTP outage would.
    /// </summary>
    private sealed class CapturingUserEmailSender() : UserEmailSenderService<User>(
        new ConfigurationBuilder().AddInMemoryCollection([new("Application:Name", "Test app")]).Build())
    {
        private int attempts;

        public bool Fail { get; init; }
        public ConcurrentQueue<SentMail> Sent { get; } = new();
        public int Attempts => attempts;

        public override Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            Interlocked.Increment(ref attempts);
            if (Fail)
                throw new InvalidOperationException("SMTP is down (test)");
            Sent.Enqueue(new SentMail(email, subject, htmlMessage));
            return Task.CompletedTask;
        }
    }
}
