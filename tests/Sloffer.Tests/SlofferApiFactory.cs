using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sloffer.Data;

namespace Sloffer.Tests;

/// <summary>
/// Runs the real API in memory against the test database.
/// The database is dropped and migrated once per test run.
/// </summary>
public sealed class SlofferApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Sloffer", TestDatabase.ConnectionString);
        builder.ConfigureTestServices(services =>
        {
            // Make sure the app can only ever talk to the test database.
            services.RemoveAll<DbContextOptions<SlofferDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<SlofferDbContext>>();
            services.AddDbContext<SlofferDbContext>(options => options.UseNpgsql(TestDatabase.ConnectionString));
        });
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<SlofferDbContext>().UseNpgsql(TestDatabase.ConnectionString).Options;
        await using var db = new SlofferDbContext(options);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<SlofferApiFactory>
{
    public const string Name = "Api";
}
