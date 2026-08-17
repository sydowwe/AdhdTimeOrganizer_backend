using AdhdTimeOrganizer.Core.domain.model.entity.@base;

namespace AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;

public class ActivityWeatherDependency : BaseLookupWithUser
{
    /// <summary>
    /// Which weather this row *means*, as one of <see cref="WeatherDependencyCodes"/> — the only thing that ties
    /// a user-editable row to a condition the forecast can be checked against.
    ///
    /// <para><b>Why a second column and not the text.</b> <see cref="BaseLookupWithUser{TUser}.Text"/> is the
    /// user's own label: they may rename "Sunny" to "Only if it's nice out", or seed the app in Slovak. Matching
    /// on text would then silently stop recommending anything. The code is written by the default seeder and
    /// never by the CRUD endpoints, so a rename cannot lose it.</para>
    ///
    /// <para><b>Null is normal.</b> A row the user created themselves has no code, and
    /// <see cref="WeatherDependencyCodes.Infer"/> guesses one from the text at read time rather than storing a
    /// guess. A row that neither carries nor infers a code simply never appears in the day's matching set —
    /// which reads as "no weather opinion" on the client, the same as having no forecast at all.</para>
    /// </summary>
    public string? Code { get; set; }
}
