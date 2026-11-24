using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sloffer.Data;
using Sloffer.Jobs.Dedup;
using Sloffer.Tests.Jobs;
using static Sloffer.Tests.Jobs.JobFetchTestSetup;

namespace Sloffer.Tests.Dedup;

/// <summary>JobDedupBackfillService against the real test database.</summary>
[Collection(ApiCollection.Name)]
public sealed class JobDedupBackfillServiceTests
{
    // Taking the collection fixture guarantees the test database is dropped and migrated first.
    public JobDedupBackfillServiceTests(SlofferApiFactory factory) => _ = factory;

    [Fact]
    public async Task Backfill_sets_keys_and_links_duplicates_oldest_first_and_is_idempotent()
    {
        await using var services = CreateServices(new ManualTimeProvider(Start), []);
        var company = $"Company {Guid.NewGuid():N}";
        var other = $"Other {Guid.NewGuid():N}";
        var source = UniqueSourceName("backfill");
        var oldest = NewJob(source, "oldest", company, "Backend Engineer", Start.AddDays(-20));
        var original = NewJob(source, "original", company, "Backend Engineer (m/f/d)", Start.AddDays(-5));
        var copy = NewJob(source, "copy", $"{company} Ltd", "Backend Engineer - Remote", Start.AddDays(-3));
        var otherCompany = NewJob(source, "other", other, "Backend Engineer", Start.AddDays(-3));
        await SeedAsync(services, copy, otherCompany, original, oldest);

        var firstRun = await Backfill(services).BackfillAsync(batchSize: 2, CancellationToken.None);

        Assert.True(firstRun >= 4);
        var stored = (await StoredJobsAsync(services, source)).ToDictionary(j => j.ExternalId);
        var key = JobDedup.NormalizeKey(company, "Backend Engineer");
        Assert.All(stored.Values, j => Assert.NotNull(j.DedupKey));
        Assert.Equal(key, stored["oldest"].DedupKey);
        Assert.Equal(key, stored["copy"].DedupKey);
        Assert.Null(stored["oldest"].DuplicateOfId);   // 15 days before "original": outside the 7-day window
        Assert.Null(stored["original"].DuplicateOfId);
        Assert.Equal(original.Id, stored["copy"].DuplicateOfId);
        Assert.Null(stored["other"].DuplicateOfId);

        var secondRun = await Backfill(services).BackfillAsync(batchSize: 2, CancellationToken.None);

        Assert.Equal(0, secondRun);
        var again = (await StoredJobsAsync(services, source)).ToDictionary(j => j.ExternalId);
        foreach (var (externalId, job) in stored)
        {
            Assert.Equal(job.DedupKey, again[externalId].DedupKey);
            Assert.Equal(job.DuplicateOfId, again[externalId].DuplicateOfId);
        }
    }

    [Fact]
    public async Task Backfill_links_old_rows_to_an_original_that_already_has_a_key_and_never_changes_it()
    {
        await using var services = CreateServices(new ManualTimeProvider(Start), []);
        var company = $"Company {Guid.NewGuid():N}";
        var source = UniqueSourceName("keyed");
        var keyed = NewJob(source, "keyed", company, "Backend Engineer", Start.AddDays(-1));
        keyed.DedupKey = JobDedup.NormalizeKey(company, "Backend Engineer");
        var unkeyed = NewJob(source, "unkeyed", company, "Backend Engineer", Start.AddDays(-2));
        await SeedAsync(services, keyed, unkeyed);

        await Backfill(services).BackfillAsync(JobDedupBackfillService.BatchSize, CancellationToken.None);

        var stored = (await StoredJobsAsync(services, source)).ToDictionary(j => j.ExternalId);
        Assert.Null(stored["keyed"].DuplicateOfId);
        Assert.Equal(keyed.DedupKey, stored["keyed"].DedupKey);
        Assert.Equal(keyed.Id, stored["unkeyed"].DuplicateOfId);
    }

    [Fact]
    public async Task StartAsync_with_BackfillDedupOnStartup_false_does_nothing()
    {
        await using var services = CreateServices(new ManualTimeProvider(Start), []);
        var source = UniqueSourceName("off");
        await SeedAsync(services, NewJob(source, "x", $"Company {Guid.NewGuid():N}", "Backend Engineer", Start.AddDays(-1)));

        await Backfill(services).StartAsync(CancellationToken.None);

        Assert.Null(Assert.Single(await StoredJobsAsync(services, source)).DedupKey);
    }

    [Fact]
    public async Task StartAsync_with_BackfillDedupOnStartup_true_backfills()
    {
        await using var services = CreateServices(new ManualTimeProvider(Start), [],
            new Dictionary<string, string?> { ["JobFetch:BackfillDedupOnStartup"] = "true" });
        var source = UniqueSourceName("on");
        await SeedAsync(services, NewJob(source, "x", $"Company {Guid.NewGuid():N}", "Backend Engineer", Start.AddDays(-1)));

        await Backfill(services).StartAsync(CancellationToken.None);

        Assert.NotNull(Assert.Single(await StoredJobsAsync(services, source)).DedupKey);
    }

    private static JobDedupBackfillService Backfill(IServiceProvider services) =>
        services.GetServices<IHostedService>().OfType<JobDedupBackfillService>().Single();

    private static Job NewJob(string source, string externalId, string company, string title, DateTimeOffset postedAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            Source = source,
            ExternalId = externalId,
            Url = $"https://example.test/{externalId}",
            Title = title,
            Company = company,
            PostedAt = postedAt,
            FetchedAt = postedAt,
        };

    private static async Task SeedAsync(IServiceProvider services, params Job[] jobs)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SlofferDbContext>();
        db.Jobs.AddRange(jobs);
        await db.SaveChangesAsync();
    }
}
