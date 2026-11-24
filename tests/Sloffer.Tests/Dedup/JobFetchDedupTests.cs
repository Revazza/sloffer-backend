using Microsoft.Extensions.DependencyInjection;
using Sloffer.Data;
using Sloffer.Jobs.Dedup;
using Sloffer.Jobs.Fetching;
using Sloffer.Tests.Jobs;
using static Sloffer.Tests.Jobs.JobFetchTestSetup;

namespace Sloffer.Tests.Dedup;

/// <summary>
/// De-duplication at insert time, against the real test database. Each test uses its own company name,
/// so keys never collide with other tests' rows.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class JobFetchDedupTests
{
    // Taking the collection fixture guarantees the test database is dropped and migrated first.
    public JobFetchDedupTests(SlofferApiFactory factory) => _ = factory;

    [Fact]
    public async Task RunCycle_same_job_from_two_sources_links_to_the_first()
    {
        var time = new ManualTimeProvider(Start);
        var company = UniqueCompany();
        var first = UniqueSourceName("first");
        var second = UniqueSourceName("second");
        await using var services = CreateServices(time, [
            FakeJobSource.Returning(first, Listing(first, "a", company, "Senior .NET Developer (m/f/d)", Start.AddHours(-2))),
            FakeJobSource.Returning(second, Listing(second, "b", $"{company} GmbH", "Senior .NET Developer - Remote", Start.AddHours(-1))),
        ]);

        await services.GetRequiredService<JobFetcher>().RunCycleAsync(CancellationToken.None);

        var original = Assert.Single(await StoredJobsAsync(services, first));
        var duplicate = Assert.Single(await StoredJobsAsync(services, second));
        Assert.Null(original.DuplicateOfId);
        Assert.Equal(original.Id, duplicate.DuplicateOfId);
        Assert.Equal(JobDedup.NormalizeKey(company, "Senior .NET Developer"), original.DedupKey);
        Assert.Equal(original.DedupKey, duplicate.DedupKey);
        Assert.NotNull(duplicate.Score); // duplicates are still stored and scored
    }

    [Fact]
    public async Task RunCycle_different_company_with_same_title_is_not_a_duplicate()
    {
        var time = new ManualTimeProvider(Start);
        var first = UniqueSourceName("first");
        var second = UniqueSourceName("second");
        await using var services = CreateServices(time, [
            FakeJobSource.Returning(first, Listing(first, "a", UniqueCompany(), "Senior .NET Developer", Start.AddHours(-2))),
            FakeJobSource.Returning(second, Listing(second, "b", UniqueCompany(), "Senior .NET Developer", Start.AddHours(-1))),
        ]);

        await services.GetRequiredService<JobFetcher>().RunCycleAsync(CancellationToken.None);

        Assert.Null(Assert.Single(await StoredJobsAsync(services, second)).DuplicateOfId);
    }

    [Fact]
    public async Task RunCycle_repost_with_new_id_within_the_window_links_to_the_original()
    {
        var time = new ManualTimeProvider(Start);
        var company = UniqueCompany();
        var name = UniqueSourceName();
        var batches = new Queue<FetchedJob[]>([
            [Listing(name, "first-post", company, "Backend Engineer", Start.AddHours(-1))],
            [Listing(name, "repost", company, "Backend Engineer", Start.AddDays(3).AddHours(-1))],
        ]);
        await using var services = CreateServices(time, [new FakeJobSource(name, _ => batches.Dequeue())]);
        var fetcher = services.GetRequiredService<JobFetcher>();

        await fetcher.RunCycleAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromDays(3));
        await fetcher.RunCycleAsync(CancellationToken.None);

        var stored = (await StoredJobsAsync(services, name)).ToDictionary(j => j.ExternalId);
        Assert.Equal(stored["first-post"].Id, stored["repost"].DuplicateOfId);
    }

    [Fact]
    public async Task RunCycle_job_outside_the_window_is_not_matched()
    {
        var time = new ManualTimeProvider(Start);
        var company = UniqueCompany();
        var name = UniqueSourceName();
        var batches = new Queue<FetchedJob[]>([
            [Listing(name, "old-post", company, "Backend Engineer", Start.AddHours(-1))],
            [Listing(name, "repost", company, "Backend Engineer", Start.AddDays(9).AddHours(-1))],
        ]);
        await using var services = CreateServices(time, [new FakeJobSource(name, _ => batches.Dequeue())]);
        var fetcher = services.GetRequiredService<JobFetcher>();

        await fetcher.RunCycleAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromDays(9));
        await fetcher.RunCycleAsync(CancellationToken.None);

        var stored = (await StoredJobsAsync(services, name)).ToDictionary(j => j.ExternalId);
        Assert.Null(stored["repost"].DuplicateOfId);
        Assert.Equal(stored["old-post"].DedupKey, stored["repost"].DedupKey);
    }

    [Fact]
    public async Task RunCycle_uses_MaxAgeDays_as_the_matching_window()
    {
        var time = new ManualTimeProvider(Start);
        var company = UniqueCompany();
        var name = UniqueSourceName();
        var batches = new Queue<FetchedJob[]>([
            [Listing(name, "first-post", company, "Backend Engineer", Start.AddHours(-1))],
            [Listing(name, "repost", company, "Backend Engineer", Start.AddDays(3).AddHours(-1))],
        ]);
        await using var services = CreateServices(time, [new FakeJobSource(name, _ => batches.Dequeue())],
            new Dictionary<string, string?> { ["JobFetch:MaxAgeDays"] = "2" });
        var fetcher = services.GetRequiredService<JobFetcher>();

        await fetcher.RunCycleAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromDays(3));
        await fetcher.RunCycleAsync(CancellationToken.None);

        var stored = (await StoredJobsAsync(services, name)).ToDictionary(j => j.ExternalId);
        Assert.Null(stored["repost"].DuplicateOfId);
    }

    [Fact]
    public async Task RunCycle_same_job_twice_in_one_batch_links_the_later_to_the_earlier()
    {
        var time = new ManualTimeProvider(Start);
        var company = UniqueCompany();
        var name = UniqueSourceName();
        await using var services = CreateServices(time, [FakeJobSource.Returning(name,
            Listing(name, "later", company, "Backend Engineer", Start.AddHours(-1)),
            Listing(name, "earlier", company, "Backend Engineer", Start.AddHours(-5)))]);

        await services.GetRequiredService<JobFetcher>().RunCycleAsync(CancellationToken.None);

        var stored = (await StoredJobsAsync(services, name)).ToDictionary(j => j.ExternalId);
        Assert.Null(stored["earlier"].DuplicateOfId);
        Assert.Equal(stored["earlier"].Id, stored["later"].DuplicateOfId);
    }

    [Fact]
    public async Task RunCycle_third_copy_links_to_the_original_not_to_a_duplicate()
    {
        var time = new ManualTimeProvider(Start);
        var company = UniqueCompany();
        var sources = new[] { UniqueSourceName("one"), UniqueSourceName("two"), UniqueSourceName("three") };
        await using var services = CreateServices(time, sources
            .Select((s, i) => (IJobSource)FakeJobSource.Returning(s, Listing(s, "x", company, "Backend Engineer", Start.AddHours(-3 + i))))
            .ToList());

        await services.GetRequiredService<JobFetcher>().RunCycleAsync(CancellationToken.None);

        var original = Assert.Single(await StoredJobsAsync(services, sources[0]));
        Assert.Equal(original.Id, Assert.Single(await StoredJobsAsync(services, sources[1])).DuplicateOfId);
        Assert.Equal(original.Id, Assert.Single(await StoredJobsAsync(services, sources[2])).DuplicateOfId);
    }

    [Fact]
    public async Task RunCycle_never_changes_the_stored_original()
    {
        var time = new ManualTimeProvider(Start);
        var company = UniqueCompany();
        var first = UniqueSourceName("first");
        var second = UniqueSourceName("second");
        var batches = new Queue<FetchedJob[]>([[], [Listing(second, "earlier-copy", company, "Backend Engineer", Start.AddHours(-10))]]);
        await using var services = CreateServices(time, [
            FakeJobSource.Returning(first, Listing(first, "stored", company, "Backend Engineer", Start.AddHours(-1))),
            new FakeJobSource(second, _ => batches.Dequeue()),
        ]);
        var fetcher = services.GetRequiredService<JobFetcher>();
        await fetcher.RunCycleAsync(CancellationToken.None);
        var before = Assert.Single(await StoredJobsAsync(services, first));

        time.Advance(TimeSpan.FromHours(1));
        await fetcher.RunCycleAsync(CancellationToken.None);

        // The new copy was posted earlier, but the stored job is never changed: the new one links to it.
        var after = Assert.Single(await StoredJobsAsync(services, first));
        Assert.Null(after.DuplicateOfId);
        Assert.Equal(before.DedupKey, after.DedupKey);
        Assert.Equal(before.Id, Assert.Single(await StoredJobsAsync(services, second)).DuplicateOfId);
    }

    private static string UniqueCompany() => $"Company {Guid.NewGuid():N}";

    private static FetchedJob Listing(string source, string externalId, string company, string title, DateTimeOffset postedAt) =>
        new(source, externalId, $"https://example.test/{externalId}", title, company,
            Location: "Spain", EmploymentTypes: null, Level: null, Description: "C#", postedAt);
}
