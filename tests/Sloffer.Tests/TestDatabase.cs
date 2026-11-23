using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Sloffer.Tests;

/// <summary>The local test database (ConnectionStrings:SlofferTest). It is dropped and re-created per test run.</summary>
internal static class TestDatabase
{
    public static string ConnectionString { get; } = Load();

    private static string Load()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets("sloffer-backend")
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("SlofferTest")
            ?? throw new InvalidOperationException(
                "Connection string 'SlofferTest' is missing. Run scripts/setup-local-db.sh or set ConnectionStrings__SlofferTest.");

        // Safety net: tests drop this database, so never let them point at a real one.
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        if (database is null || !database.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Test database name must end with '_test' (got '{database}').");

        return connectionString;
    }
}
