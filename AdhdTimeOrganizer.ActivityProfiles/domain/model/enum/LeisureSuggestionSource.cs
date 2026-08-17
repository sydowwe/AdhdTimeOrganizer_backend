namespace AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;

/// <summary>
/// Which of the three profile tables a leisure suggestion was drawn from. The draw ranks all three
/// against each other, so the source has to travel with the candidate — it decides the eligibility
/// floor, the source weight and what the card is allowed to render.
/// </summary>
public enum LeisureSuggestionSource
{
    Backlog,
    Project,
    BucketList
}
