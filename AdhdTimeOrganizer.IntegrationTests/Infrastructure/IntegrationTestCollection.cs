using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Infrastructure;

[CollectionDefinition("Integration")]
public class IntegrationTestCollection : ICollectionFixture<TestWebApplicationFactory> { }
