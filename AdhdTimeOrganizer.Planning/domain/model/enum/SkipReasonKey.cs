namespace AdhdTimeOrganizer.Planning.domain.model.@enum;

/// <summary>
/// The three built-in skip reasons the home widget offers, stored as a key instead of as translated copy — so a
/// reworded label can no longer split one reason into two groups. Any other reason stays free text in
/// <c>PlannerTask.SkipReason</c>; at most one of the two is ever set.
/// <para>
/// Stored as the member name (<c>EnumColumn</c>). The wire spelling is camelCase and is mapped explicitly in
/// <c>SkipReasonKeyTokens</c>, not by the serializer.
/// </para>
/// </summary>
public enum SkipReasonKey
{
    NoTime,
    NotRelevant,
    NoEnergy
}

/// <summary>
/// The labels the client has shipped for each key, in every locale it ships. A free-text reason equal to one of
/// them <b>exactly</b> (it is already trimmed) is stored as that key, never as text — the contract is that these
/// six strings do not come back as text. Near-misses ("ran out of time again", different casing) stay text; the
/// client's fold handles those.
/// <para>
/// The <c>SkipReasonKeys</c> migration converted existing rows with this same table as it stood then. Adding a
/// locale or rewording a label means adding a row here <b>and</b> a data migration for rows already written.
/// </para>
/// </summary>
public static class SkipReasonKeyLabels
{
    private static readonly Dictionary<string, SkipReasonKey> KeysByLabel = new(StringComparer.Ordinal)
    {
        ["Nestihol som"] = SkipReasonKey.NoTime,
        ["Ran out of time"] = SkipReasonKey.NoTime,
        ["Už nie je aktuálne"] = SkipReasonKey.NotRelevant,
        ["Not relevant any more"] = SkipReasonKey.NotRelevant,
        ["Nemal som energiu"] = SkipReasonKey.NoEnergy,
        ["No energy for it"] = SkipReasonKey.NoEnergy
    };

    public static bool TryGetKey(string label, out SkipReasonKey key) => KeysByLabel.TryGetValue(label, out key);
}
