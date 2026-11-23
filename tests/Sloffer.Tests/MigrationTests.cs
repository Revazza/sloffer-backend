using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sloffer.Data;

namespace Sloffer.Tests;

[Collection(ApiCollection.Name)]
public sealed class MigrationTests(SlofferApiFactory factory)
{
    [Fact]
    public void Model_has_no_changes_missing_a_migration()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlofferDbContext>();

        Assert.False(db.Database.HasPendingModelChanges(),
            "The EF model changed without a migration. Run: dotnet ef migrations add <Name> --project src/Sloffer.Data");
    }

    [Fact]
    public async Task All_migrations_are_applied_to_the_test_database()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SlofferDbContext>();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
}
