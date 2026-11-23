using Sloffer.Data;

namespace Sloffer.Jobs;

/// <summary>Placeholder until real jobs exist: checks the database is reachable at startup.</summary>
public sealed class DatabaseCheckService(IServiceScopeFactory scopes, ILogger<DatabaseCheckService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SlofferDbContext>();

        if (await db.Database.CanConnectAsync(stoppingToken))
            logger.LogInformation("Database reachable. No jobs configured yet.");
        else
            logger.LogError("Cannot connect to the database. Check ConnectionStrings:Sloffer.");
    }
}
