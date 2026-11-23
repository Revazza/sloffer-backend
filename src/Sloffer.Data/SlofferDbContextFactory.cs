using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Sloffer.Data;

/// <summary>
/// Used only by `dotnet ef` (migrations add / database update). Reads the same user-secrets as the apps.
/// </summary>
public sealed class SlofferDbContextFactory : IDesignTimeDbContextFactory<SlofferDbContext>
{
    private const string FallbackConnectionString = "Host=localhost;Port=5432;Database=sloffer;Username=sloffer";

    public SlofferDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets("sloffer-backend")
            .AddEnvironmentVariables()
            .Build();

        // `migrations add` doesn't need a real connection, so a fallback is fine; `database update` does.
        var connectionString = configuration.GetConnectionString(DataServiceCollectionExtensions.ConnectionStringName)
            ?? FallbackConnectionString;

        var options = new DbContextOptionsBuilder<SlofferDbContext>().UseNpgsql(connectionString).Options;
        return new SlofferDbContext(options);
    }
}
