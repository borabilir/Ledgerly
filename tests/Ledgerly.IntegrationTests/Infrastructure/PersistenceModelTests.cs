using Ledgerly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ledgerly.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PersistenceModelTests(PostgresFixture fixture)
{
    [Fact]
    public async Task CurrentModel_ShouldMatchMigrationSnapshot()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
