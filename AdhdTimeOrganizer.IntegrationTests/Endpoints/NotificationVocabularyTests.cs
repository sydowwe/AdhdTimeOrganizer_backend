using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Notifications.Testing.baseTests;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// This host's run of the module's vocabulary contract (backend ask B15) — every kind the portal raises is
/// worded in every locale, through the tables <c>ModuleServiceExtensions.AddNotificationVocabulary</c>
/// registers. The rules live in <see cref="BaseNotificationVocabularyTests"/>; all this host owes it is the
/// catalog, the same second copy <see cref="NotificationCatalogTests"/> keeps.
/// </summary>
[Collection("Postgres")]
public class NotificationVocabularyTests(AppDbContextFixture fixture) : BaseNotificationVocabularyTests(fixture)
{
    protected override IReadOnlySet<NotificationType> ExpectedCatalog => NotificationCatalogTests.Catalog;
}
