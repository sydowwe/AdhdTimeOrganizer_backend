using Sydowwe.Framework.domain.result;

namespace Sydowwe.Framework.domain.serviceContract;

/// <summary>
/// Creates the per-user rows a brand-new account needs before it is usable. Called inside the
/// registration transaction, so a failure rolls the account creation back with it.
///
/// <para>Which defaults those are is entirely the host's business — this contract exists so the
/// Framework sign-up endpoints can invoke the step without knowing what it does.</para>
/// </summary>
public interface IUserDefaultsService
{
    Task<Result> CreateDefaultsAsync(long userId, CancellationToken ct = default);
}
