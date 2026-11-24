using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sloffer.Data;
using Sloffer.Jobs.Matching;
using Sloffer.Tests.Jobs;
using static Sloffer.Tests.Jobs.JobFetchTestSetup;

namespace Sloffer.Tests.Matching;

/// <summary>JobRescoreService against the real test database.</summary>
[Collection(ApiCollection.Name)]
public sealed class JobRescoreServiceTests
{
    private static readonly DateTimeOffset Earlier = Start.AddDays(-1);

    // Taking the collection fixture guarantees the test database is dropped and migrated first.
    public JobRescoreServiceTests(SlofferApiFactory factory) => _ = factory;

    [Fact]
    public async Task RescoreOutdated_scores_unscored_and_outdated_rows_in_batches()
    {
        var time = new ManualTimeProvider(Start);
        await using var services = CreateServices(time, [], new Dictionary<string, string?> { ["JobScoring:RescoreBatchSize"] = "2" });
        var name = UniqueSourceName("rescore");
        await SeedAsync(services,
            NewJob(name, "unscored-1"),
            NewJob(name, "unscored-2"),
            NewJob(name, "unscored-3"),
            NewJob(name, "old-version", score: 99, version: JobScorer.Version - 1));

        var updated = await RescoreService(services).RescoreOutdatedAsync(CancellationToken.None);

        Assert.True(updated >= 4);
        var stored = await StoredJobsAsync(services, name);
        Assert.All(stored, job =>
        {
            Assert.Equal(40 + 15 + 5 + 5, job.Score); // .NET in title, no level word, "3+ years"
            Assert.Equal(["title mentions .NET/C# (+15)", "no level in title (+5)", "asks 3+ years (+5)"], job.ScoreReasons);
            Assert.Equal(JobScorer.Version, job.ScoreVersion);
            Assert.Equal(Start, job.ScoredAt);
        });
        Assert.Equal(0, await RescoreService(services).RescoreOutdatedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RescoreOutdated_leaves_rows_with_current_version_unchanged()
    {
        var time = new ManualTimeProvider(Start);
        await using var services = CreateServices(time, []);
        var name = UniqueSourceName("current");
        await SeedAsync(services, NewJob(name, "current", score: 77, version: JobScorer.Version, reasons: ["kept"]));

        await RescoreService(services).RescoreOutdatedAsync(CancellationToken.None);

        var job = Assert.Single(await StoredJobsAsync(services, name));
        Assert.Equal(77, job.Score);
        Assert.Equal(["kept"], job.ScoreReasons);
        Assert.Equal(Earlier, job.ScoredAt);
    }

    [Fact]
    public async Task StartAsync_with_RescoreOnStartup_false_does_nothing()
    {
        var time = new ManualTimeProvider(Start);
        await using var services = CreateServices(time, []);
        var name = UniqueSourceName("off");
        await SeedAsync(services, NewJob(name, "unscored"));

        await RescoreService(services).StartAsync(CancellationToken.None);

        Assert.Null(Assert.Single(await StoredJobsAsync(services, name)).Score);
    }

    [Fact]
    public async Task StartAsync_with_RescoreOnStartup_true_rescores()
    {
        var time = new ManualTimeProvider(Start);
        await using var services = CreateServices(time, [], new Dictionary<string, string?> { ["JobScoring:RescoreOnStartup"] = "true" });
        var name = UniqueSourceName("on");
        await SeedAsync(services, NewJob(name, "unscored"));

        await RescoreService(services).StartAsync(CancellationToken.None);

        Assert.NotNull(Assert.Single(await StoredJobsAsync(services, name)).Score);
    }

    [Fact]
    public void AddJobFetching_registers_backfill_and_rescore_before_the_fetch_worker()
    {
        using var services = CreateServices(new ManualTimeProvider(Start), []);

        var hosted = services.GetServices<IHostedService>().Select(s => s.GetType().Name).ToList();

        Assert.Equal(["JobDedupBackfillService", "JobRescoreService", "JobFetchService"], hosted);
    }

    private static JobRescoreService RescoreService(IServiceProvider services) =>
        services.GetServices<IHostedService>().OfType<JobRescoreService>().Single();

    private static Job NewJob(string source, string externalId, int? score = null, int? version = null, List<string>? reasons = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Source = source,
            ExternalId = externalId,
            Url = $"https://example.test/{externalId}",
            Title = ".NET Developer",
            Company = "Example Co",
            Location = "Spain",
            Description = "<p>3+ years building APIs.</p>",
            PostedAt = Earlier,
            FetchedAt = Earlier,
            Score = score,
            ScoreReasons = reasons ?? (score is null ? null : ["old"]),
            ScoreVersion = version,
            ScoredAt = score is null ? null : Earlier,
        };

    private static async Task SeedAsync(IServiceProvider services, params Job[] jobs)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SlofferDbContext>();
        db.Jobs.AddRange(jobs);
        await db.SaveChangesAsync();
    }
}
