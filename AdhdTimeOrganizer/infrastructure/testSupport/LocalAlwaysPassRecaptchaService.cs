using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.result;

namespace AdhdTimeOrganizer.infrastructure.testSupport;

/// <summary>
/// An <see cref="IGoogleRecaptchaService"/> that verifies nothing and always succeeds. Registered
/// <b>only</b> when <see cref="TestSeedGate"/> is open — i.e. in the same processes that are already
/// allowed to expose the database-truncating <c>POST /api/test/seed</c>.
///
/// <para><b>Why this exists.</b> The real <c>GoogleRecaptchaService</c> always calls Google's
/// <c>siteverify</c> and then requires three things of the answer: <c>success</c>, an <c>action</c>
/// equal to the one the endpoint asked for, and a <c>score</c> at or above the threshold. A local
/// developer has no v3 key pair, so the client sends an empty token and login fails with
/// <c>400 "Recaptcha verification failed."</c> — which made <b>every</b> local sign-in impossible,
/// the Playwright suite included, since its auth fixture drives the real login form.</para>
///
/// <para><b>Why the v2 test keys are not the answer.</b> Google's well-known always-pass pair is
/// reCAPTCHA <b>v2</b>; its <c>siteverify</c> response carries neither <c>action</c> nor <c>score</c>.
/// Against this app's v3 checks it clears <c>success</c> and then fails the action comparison. There is
/// no v3 equivalent, so no combination of secrets makes the real service pass locally.</para>
///
/// <para><b>Why it is gated on <see cref="TestSeedGate"/> rather than on
/// <c>IsDevelopment()</c>.</b> The gate already answers a strictly stronger question — Development (or
/// an explicitly allow-listed environment, never Production or Staging) <i>and</i> a database on this
/// machine. Anything permitted to wipe its own database is, by construction, not an environment whose
/// bot protection is load-bearing. Reusing it means there is one switch to reason about instead of two,
/// and no new configuration key that could be set independently of it.</para>
///
/// <para>⚠ Do not widen this to a config flag or an environment-name check. The registration in
/// <c>Program.ConfigureServices</c> is the whole control; a flag would be a way to disable bot
/// protection on a host that the seed gate refuses.</para>
/// </summary>
public sealed class LocalAlwaysPassRecaptchaService : IGoogleRecaptchaService
{
    public Task<Result> VerifyRecaptchaAsync(string token, string expectedAction)
    {
        return Task.FromResult(Result.Successful());
    }
}
