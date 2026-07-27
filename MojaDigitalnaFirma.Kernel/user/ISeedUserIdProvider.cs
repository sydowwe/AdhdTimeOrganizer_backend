namespace MojaDigitalnaFirma.Kernel.user;

/// <summary>
/// Supplies existing user ids to dev seeders that need someone to own their fixture rows.
/// <para>
/// The modules key everything by a plain <c>UserId</c> and deliberately do not know the host's concrete
/// user type — but a dev seeder still has to point its fixtures at real users. Rather than give the
/// modules a user entity back just for seeding, the host implements this and the seeders ask for ids.
/// </para>
/// </summary>
public interface ISeedUserIdProvider
{
    /// <summary>
    /// The first <paramref name="count"/> user ids ordered by id, or an empty list when the host has no
    /// users yet — in which case the caller should skip seeding rather than invent an owner.
    /// </summary>
    Task<IReadOnlyList<long>> GetSeedUserIdsAsync(int count, CancellationToken ct = default);
}