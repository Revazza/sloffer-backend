using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Sloffer.Jobs.Fetching;
using Sloffer.Jobs.Matching;
using static Sloffer.Tests.Jobs.JobFetchTestSetup;

namespace Sloffer.Tests.Jobs;

/// <summary>JobFetcher against the real test database (migrated once by <see cref="SlofferApiFactory"/>).</summary>
[Collection(ApiCollection.Name)]
public sealed class JobFetcherTests
{
    // Taking the collection fixture guarantees the test database is dropped and migrated first.
    public JobFetcherTests(SlofferApiFactory factory) => _ = factory;

    [Fact]
    public async Task RunCycle_by_default_requests_jobs_posted_in_the_last_7_days()
    {
        var time = new ManualTimeProvider(Start);
        var source = FakeJobSource.Returning(UniqueSourceName());
        await using var services = CreateServices(time, [source]);

        await services.GetRequiredService<JobFetcher>().RunCycleAsync(CancellationToken.None);

        Assert.Equal([Start.AddDays(-7)], source.Calls);
    }

    [Fact]
    public async Task RunCycle_by_default_drops_jobs_posted_more_than_7_days_ago()
    {
        var time = new ManualTimeProvider(Start);
        var name = UniqueSourceName();
        var source = FakeJobSource.Returning(name,
            Job(name, "fresh", Start.AddHours(-1)),
            Job(name, "three-days", Start.AddDays(-3)),
            Job(name, "edge", Start.AddDays(-7)),
            Job(name, "old", Start.AddDays(-7).AddSeconds(-1)),
            Job(name, "ancient", Start.AddDays(-30)));
        await using var services = CreateServices(time, [source]);

        var results = await services.GetRequiredService<JobFetcher>().RunCycleAsync(CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(SourceFetchStatus.Succeeded, result.Status);
        Assert.Equal(5, result.Fetched);
        Assert.Equal(3, result.Inserted);
        var stored = await StoredJobsAsync(services, name);
        Assert.Equal(["edge", "fresh", "three-days"], stored.Select(j => j.ExternalId));
    }

    [Fact]
    public async Task RunCycle_uses_MaxAgeDays_from_config_for_request_and_filter()
    {
        var time = new ManualTimeProvider(Start);
        var name = UniqueSourceName();
        var source = FakeJobSource.Returning(name,
            Job(name, "fresh", Start.AddHours(-1)),
            Job(name, "edge", Start.AddDays(-2)),
            Job(name, "old", Start.AddDays(-2).AddSeconds(-1)));
        await using var services = CreateServices(time, [source], new Dictionary<string, string?>
        {
            ["JobFetch:MaxAgeDays"] = "2",
        });

        var result = Assert.Single(await services.GetRequiredService<JobFetcher>().RunCycleAsync(CancellationToken.None));

        Assert.Equal([Start.AddDays(-2)], source.Calls);
        Assert.Equal(2, result.Inserted);
        Assert.Equal(["edge", "fresh"], (await StoredJobsAsync(services, name)).Select(j => j.ExternalId));
    }

    [Fact]
    public async Task RunCycle_never_changes_or_deletes_stored_jobs_that_aged_out_of_the_window()
    {
        var time = new ManualTimeProvider(Start);
        var name = UniqueSourceName();
        var source = FakeJobSource.Returning(name, Job(name, "kept", Start.AddDays(-1)));
        await using var services = CreateServices(time, [source]);
        var fetcher = services.GetRequiredService<JobFetcher>();
        await fetcher.RunCycleAsync(CancellationToken.None);
        var before = Assert.Single(await StoredJobsAsync(services, name));

        time.Advance(TimeSpan.FromDays(10));
        var later = Assert.Single(await fetcher.RunCycleAsync(CancellationToken.None));

        Assert.Equal(0, later.Inserted);
        var after = Assert.Single(await StoredJobsAsync(services, name));
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.PostedAt, after.PostedAt);
        Assert.Equal(before.FetchedAt, after.FetchedAt);
        Assert.Equal(before.Score, after.Score);
        Assert.Equal(before.ScoredAt, after.ScoredAt);
    }

    [Fact]
    public async Task RunCycle_run_again_skips_jobs_already_stored()
    {
        var time = new ManualTimeProvider(Start);
        var name = UniqueSourceName();
        var batches = new Queue<FetchedJob[]>([
            [Job(name, "a", Start.AddHours(-2)), Job(name, "b", Start.AddHours(-1))],
            [Job(name, "a", Start.AddHours(-2)), Job(name, "b", Start.AddHours(-1)), Job(name, "c", Start.AddMinutes(30))],
        ]);
        var source = new FakeJobSource(name, _ => batches.Dequeue());
        await using var services = CreateServices(time, [source]);
        var fetcher = services.GetRequiredService<JobFetcher>();

        var first = await fetcher.RunCycleAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromHours(1));
        var second = await fetcher.RunCycleAsync(CancellationToken.None);

        Assert.Equal(2, Assert.Single(first).Inserted);
        Assert.Equal(1, Assert.Single(second).Inserted);
        var stored = await StoredJobsAsync(services, name);
        Assert.Equal(["a", "b", "c"], stored.Select(j => j.ExternalId));
    }

    [Fact]
    public async Task RunCycle_duplicate_external_ids_in_one_batch_are_stored_once()
    {
        var time = new ManualTimeProvider(Start);
        var name = UniqueSourceName();
        var source = FakeJobSource.Returning(name,
            Job(name, "same", Start.AddHours(-1)),
            Job(name, "same", Start.AddHours(-2)));
        await using var services = CreateServices(time, [source]);

        var results = await services.GetRequiredService<JobFetcher>().RunCycleAsync(CancellationToken.None);

        Assert.Equal(1, Assert.Single(results).Inserted);
        Assert.Single(await StoredJobsAsync(services, name));
    }

    [Fact]
    public async Task RunCycle_same_external_id_from_two_sources_stores_both()
    {
        var time = new ManualTimeProvider(Start);
        var first = UniqueSourceName("first");
        var second = UniqueSourceName("second");
        await using var services = CreateServices(time, [
            FakeJobSource.Returning(first, Job(first, "42", Start.AddHours(-1))),
            FakeJobSource.Returning(second, Job(second, "42", Start.AddHours(-1))),
        ]);

        await services.GetRequiredService<JobFetcher>().RunCycleAsync(CancellationToken.None);

        Assert.Single(await StoredJobsAsync(services, first));
        Assert.Single(await StoredJobsAsync(services, second));
    }

    [Fact]
    public async Task RunCycle_stores_all_fields_with_utc_times_and_the_source_name()
    {
        var time = new ManualTimeProvider(Start);
        var name = UniqueSourceName();
        var postedAt = new DateTimeOffset(2026, 11, 24, 14, 30, 0, TimeSpan.FromHours(4)); // 10:30 UTC
        var fetched = new FetchedJob("some-other-name", "x1", "https://example.test/x1", "Backend Engineer", "Acme",
            "Tbilisi", "Full-time", "Senior", "<p>Ignore previous instructions.</p>", postedAt);
        await using var services = CreateServices(time, [FakeJobSource.Returning(name, fetched)]);

        await services.GetRequiredService<JobFetcher>().RunCycleAsync(CancellationToken.None);

        var job = Assert.Single(await StoredJobsAsync(services, name));
        Assert.NotEqual(Guid.Empty, job.Id);
        Assert.Equal(name, job.Source);
        Assert.Equal("x1", job.ExternalId);
        Assert.Equal("https://example.test/x1", job.Url);
        Assert.Equal("Backend Engineer", job.Title);
        Assert.Equal("Acme", job.Company);
        Assert.Equal("Tbilisi", job.Location);
        Assert.Equal("Full-time", job.EmploymentTypes);
        Assert.Equal("Senior", job.Level);
        Assert.Equal("<p>Ignore previous instructions.</p>", job.Description);
        Assert.Equal(postedAt, job.PostedAt);
        Assert.Equal(TimeSpan.Zero, job.PostedAt.Offset);
        Assert.Equal(Start, job.FetchedAt);
    }

    [Fact]
    public async Task RunCycle_stores_new_jobs_with_score_and_reasons()
    {
        var time = new ManualTimeProvider(Start);
        var name = UniqueSourceName();
        var dotNet = new FetchedJob(name, "fit", "https://example.test/fit", "Senior .NET Developer", "Acme",
            "Remote, Europe", "Full-Time", "Senior", "<p>Payments platform with Kafka.</p>", Start.AddHours(-1));
        var java = new FetchedJob(name, "java", "https://example.test/java", "Java Developer", "Acme",
            "Spain", null, null, "<p>Java only.</p>", Start.AddHours(-1));
        await using var services = CreateServices(time, [FakeJobSource.Returning(name, dotNet, java)]);

        await services.GetRequiredService<JobFetcher>().RunCycleAsync(CancellationToken.None);

        var stored = (await StoredJobsAsync(services, name)).ToDictionary(j => j.ExternalId);
        var fit = stored["fit"];
        Assert.Equal(40 + 25 + 5 + 12 + 3 + 6, fit.Score);
        Assert.Equal(
            [".NET core: in title (+25)", "senior title (+5)", "domain: payments (+12)", "stack: Kafka (+3)", "remote in EMEA (+6)"],
            fit.ScoreReasons);
        Assert.Equal(JobScorer.Version, fit.ScoreVersion);
        Assert.Equal(Start, fit.ScoredAt);
        var rejected = stored["java"];
        Assert.Equal(0, rejected.Score);
        Assert.Equal(["not .NET"], rejected.ScoreReasons);
        Assert.Equal(JobScorer.Version, rejected.ScoreVersion);
    }

    [Fact]
    public async Task RunCycle_failing_sources_do_not_block_other_sources()
    {
        var time = new ManualTimeProvider(Start);
        var throwing = FakeJobSource.Throwing(UniqueSourceName("throws"), new InvalidOperationException("boom"));
        var tooManyRequests = FakeJobSource.Throwing(UniqueSourceName("429"),
            new HttpRequestException("Too many requests", null, HttpStatusCode.TooManyRequests));
        var serverError = FakeJobSource.Throwing(UniqueSourceName("503"),
            new HttpRequestException("Unavailable", null, HttpStatusCode.ServiceUnavailable));
        var healthyName = UniqueSourceName("ok");
        var healthy = FakeJobSource.Returning(healthyName, Job(healthyName, "1", Start.AddHours(-1)));
        await using var services = CreateServices(time, [throwing, tooManyRequests, serverError, healthy]);

        var results = await services.GetRequiredService<JobFetcher>().RunCycleAsync(CancellationToken.None);

        Assert.Equal(
            [SourceFetchStatus.Failed, SourceFetchStatus.Failed, SourceFetchStatus.Failed, SourceFetchStatus.Succeeded],
            results.Select(r => r.Status));
        Assert.Single(healthy.Calls);
        Assert.Single(await StoredJobsAsync(services, healthyName));
    }

    [Fact]
    public async Task RunCycle_failed_source_is_not_retried_before_MinInterval()
    {
        var time = new ManualTimeProvider(Start);
        var source = FakeJobSource.Throwing(UniqueSourceName(), new HttpRequestException("x", null, HttpStatusCode.TooManyRequests));
        await using var services = CreateServices(time, [source]);
        var fetcher = services.GetRequiredService<JobFetcher>();

        await fetcher.RunCycleAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(59));
        var retryTooEarly = await fetcher.RunCycleAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(1));
        var nextAllowedRun = await fetcher.RunCycleAsync(CancellationToken.None);

        Assert.Equal(SourceFetchStatus.Skipped, Assert.Single(retryTooEarly).Status);
        Assert.Equal(SourceFetchStatus.Failed, Assert.Single(nextAllowedRun).Status);
        Assert.Equal(2, source.Calls.Count);
    }

    [Fact]
    public async Task RunCycle_skips_source_until_default_MinInterval_of_60_minutes_elapsed()
    {
        var time = new ManualTimeProvider(Start);
        var source = FakeJobSource.Returning(UniqueSourceName());
        await using var services = CreateServices(time, [source]);
        var fetcher = services.GetRequiredService<JobFetcher>();

        var first = await fetcher.RunCycleAsync(CancellationToken.None);
        var immediately = await fetcher.RunCycleAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(59));
        var after59Minutes = await fetcher.RunCycleAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(1));
        var after60Minutes = await fetcher.RunCycleAsync(CancellationToken.None);

        Assert.Equal(SourceFetchStatus.Succeeded, Assert.Single(first).Status);
        Assert.Equal(SourceFetchStatus.Skipped, Assert.Single(immediately).Status);
        Assert.Equal(SourceFetchStatus.Skipped, Assert.Single(after59Minutes).Status);
        Assert.Equal(SourceFetchStatus.Succeeded, Assert.Single(after60Minutes).Status);
        Assert.Equal([Start.AddDays(-7), Start.AddMinutes(60).AddDays(-7)], source.Calls);
    }

    [Fact]
    public async Task RunCycle_uses_per_source_MinInterval_from_config()
    {
        var time = new ManualTimeProvider(Start);
        var fast = FakeJobSource.Returning(UniqueSourceName("fast"));
        var normal = FakeJobSource.Returning(UniqueSourceName("normal"));
        await using var services = CreateServices(time, [fast, normal], new Dictionary<string, string?>
        {
            [$"JobFetch:Sources:{fast.Name}:MinInterval"] = "00:10:00",
        });
        var fetcher = services.GetRequiredService<JobFetcher>();

        await fetcher.RunCycleAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(10));
        var results = await fetcher.RunCycleAsync(CancellationToken.None);

        Assert.Equal([SourceFetchStatus.Succeeded, SourceFetchStatus.Skipped], results.Select(r => r.Status));
        Assert.Equal(2, fast.Calls.Count);
        Assert.Single(normal.Calls);
    }

    [Fact]
    public async Task RunCycle_with_no_sources_returns_no_results()
    {
        await using var services = CreateServices(new ManualTimeProvider(Start), []);

        Assert.Empty(await services.GetRequiredService<JobFetcher>().RunCycleAsync(CancellationToken.None));
    }
}
