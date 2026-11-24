using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sloffer.Data;
using Sloffer.Jobs.Fetching;
using Sloffer.Jobs.Sources.Jobicy;
using static Sloffer.Tests.Jobs.JobFetchTestSetup;

namespace Sloffer.Tests.Jobs.Jobicy;

/// <summary>JobFetcher + the real Jobicy registration (stubbed HTTP) against the test database.</summary>
[Collection(ApiCollection.Name)]
public sealed class JobicyFetchIntegrationTests
{
    public JobicyFetchIntegrationTests(SlofferApiFactory factory) => _ = factory;

    private static ServiceProvider CreateServices(TimeProvider time, HttpMessageHandler handler, IDictionary<string, string?>? config = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config ?? new Dictionary<string, string?>()).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<SlofferDbContext>(o => o.UseNpgsql(TestDatabase.ConnectionString));
        services.AddSingleton(time);
        services.AddJobFetching(configuration);
        services.AddJobicySource(configuration).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public async Task RunCycle_http_429_marks_source_failed_and_waits_an_hour()
    {
        var time = new ManualTimeProvider(Start);
        var handler = StubHttpMessageHandler.Status(HttpStatusCode.TooManyRequests);
        await using var services = CreateServices(time, handler);
        var fetcher = services.GetRequiredService<JobFetcher>();

        var first = await fetcher.RunCycleAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(59));
        var second = await fetcher.RunCycleAsync(CancellationToken.None);

        Assert.Equal(new SourceFetchResult("jobicy", SourceFetchStatus.Failed), Assert.Single(first));
        Assert.Equal(SourceFetchStatus.Skipped, Assert.Single(second).Status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RunCycle_with_default_7_day_window_stores_all_sample_jobicy_jobs()
    {
        // Start is 2026-11-24T12:00Z, so the default cut-off is 2026-11-17T12:00Z: all three sample listings qualify.
        var time = new ManualTimeProvider(Start);
        await DeleteStoredJobicyJobsAsync();
        var handler = StubHttpMessageHandler.Json(JobicyTestSetup.SampleResponse());
        await using var services = CreateServices(time, handler);

        var result = Assert.Single(await services.GetRequiredService<JobFetcher>().RunCycleAsync(CancellationToken.None));

        Assert.Equal(3, result.Inserted);
        var stored = await StoredJobsAsync(services, JobicyJobSource.SourceName);
        Assert.Contains(stored, j => j.ExternalId == "160001");
    }

    [Fact]
    public async Task RunCycle_stores_recent_jobicy_jobs_with_source_and_url()
    {
        // MaxAgeDays = 1 and Start is 2026-11-24T12:00Z, so the cut-off is 2026-11-23T12:00Z: two of the three sample listings qualify.
        var time = new ManualTimeProvider(Start);
        await DeleteStoredJobicyJobsAsync();
        var handler = StubHttpMessageHandler.Json(JobicyTestSetup.SampleResponse());
        await using var services = CreateServices(time, handler, new Dictionary<string, string?> { ["JobFetch:MaxAgeDays"] = "1" });

        var result = Assert.Single(await services.GetRequiredService<JobFetcher>().RunCycleAsync(CancellationToken.None));

        Assert.Equal(SourceFetchStatus.Succeeded, result.Status);
        var stored = await StoredJobsAsync(services, JobicyJobSource.SourceName);
        Assert.Equal(["163960", "164017"], stored.Select(j => j.ExternalId));
        Assert.Equal("https://jobicy.com/jobs/164017-mulesoft-development-engineer", stored[1].Url);
    }

    /// <summary>Both store tests write Source = jobicy to the shared test database, so each starts from none.</summary>
    private static async Task DeleteStoredJobicyJobsAsync()
    {
        var options = new DbContextOptionsBuilder<SlofferDbContext>().UseNpgsql(TestDatabase.ConnectionString).Options;
        await using var db = new SlofferDbContext(options);
        await db.Jobs.Where(j => j.Source == JobicyJobSource.SourceName).ExecuteDeleteAsync();
    }
}
