using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sloffer.Data;
using Sloffer.Jobs.Fetching;
using Sloffer.Jobs.Sources.Arbeitnow;
using Sloffer.Tests.Jobs.Jobicy;
using static Sloffer.Tests.Jobs.JobFetchTestSetup;

namespace Sloffer.Tests.Jobs.Arbeitnow;

/// <summary>JobFetcher + the real Arbeitnow registration (stubbed HTTP) against the test database.</summary>
[Collection(ApiCollection.Name)]
public sealed class ArbeitnowFetchIntegrationTests
{
    public ArbeitnowFetchIntegrationTests(SlofferApiFactory factory) => _ = factory;

    /// <summary>Fixture listings are posted 2026-11-24T00:31Z to 2026-11-25T00:55Z.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 3, 0, 0, TimeSpan.Zero);

    private static ServiceProvider CreateServices(
        TimeProvider time,
        HttpMessageHandler handler,
        IEnumerable<IJobSource>? otherSources = null,
        IDictionary<string, string?>? config = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config ?? new Dictionary<string, string?>()).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<SlofferDbContext>(o => o.UseNpgsql(TestDatabase.ConnectionString));
        services.AddSingleton(time);
        services.AddJobFetching(configuration);
        services.AddArbeitnowSource(configuration).ConfigurePrimaryHttpMessageHandler(() => handler);
        foreach (var source in otherSources ?? [])
            services.AddSingleton(source);
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public async Task RunCycle_failing_arbeitnow_does_not_block_other_sources()
    {
        var time = new ManualTimeProvider(Now);
        var other = UniqueSourceName();
        var otherSource = FakeJobSource.Returning(other, Job(other, "1", Now.AddHours(-1)));
        var handler = StubHttpMessageHandler.Status(HttpStatusCode.InternalServerError);
        await using var services = CreateServices(time, handler, [otherSource]);

        var results = await services.GetRequiredService<JobFetcher>().RunCycleAsync(CancellationToken.None);

        Assert.Equal(new SourceFetchResult("Arbeitnow", SourceFetchStatus.Failed), results.Single(r => r.Source == "Arbeitnow"));
        Assert.Equal(new SourceFetchResult(other, SourceFetchStatus.Succeeded, 1, 1), results.Single(r => r.Source == other));
        Assert.Single(await StoredJobsAsync(services, other));
    }

    [Fact]
    public async Task RunCycle_http_429_marks_source_failed_and_waits_MinInterval()
    {
        var time = new ManualTimeProvider(Now);
        var handler = StubHttpMessageHandler.Status(HttpStatusCode.TooManyRequests);
        await using var services = CreateServices(time, handler);
        var fetcher = services.GetRequiredService<JobFetcher>();

        var first = await fetcher.RunCycleAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(59));
        var second = await fetcher.RunCycleAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(1));
        var third = await fetcher.RunCycleAsync(CancellationToken.None);

        Assert.Equal(SourceFetchStatus.Failed, Assert.Single(first).Status);
        Assert.Equal(SourceFetchStatus.Skipped, Assert.Single(second).Status);
        Assert.Equal(SourceFetchStatus.Failed, Assert.Single(third).Status);
        Assert.Equal(2, handler.Requests.Count); // no retry in between
    }

    [Fact]
    public async Task RunCycle_stores_keyword_matching_arbeitnow_jobs_with_source_and_url()
    {
        var time = new ManualTimeProvider(Now);
        await DeleteStoredArbeitnowJobsAsync();
        var handler = StubHttpMessageHandler.Json(ArbeitnowTestSetup.SampleResponse());
        await using var services = CreateServices(time, handler);

        var result = Assert.Single(await services.GetRequiredService<JobFetcher>().RunCycleAsync(CancellationToken.None));

        Assert.Equal(new SourceFetchResult("Arbeitnow", SourceFetchStatus.Succeeded, 3, 3), result);
        var stored = await StoredJobsAsync(services, ArbeitnowJobSource.SourceName);
        Assert.Equal(
            ["dotnet-developer-munich-458003", "remote-csharp-engineer-458002", "senior-net-backend-developer-fintech-berlin-458001"],
            stored.Select(j => j.ExternalId));
        Assert.Equal("https://www.arbeitnow.com/jobs/companies/hybrid-ag/dotnet-developer-munich-458003", stored[0].Url);
        Assert.Equal("Munich, Remote", stored[0].Location);
        Assert.Equal(new DateTimeOffset(2026, 11, 24, 0, 31, 17, TimeSpan.Zero), stored[0].PostedAt);
    }

    /// <summary>Store tests write Source = Arbeitnow to the shared test database, so each starts from none.</summary>
    private static async Task DeleteStoredArbeitnowJobsAsync()
    {
        var options = new DbContextOptionsBuilder<SlofferDbContext>().UseNpgsql(TestDatabase.ConnectionString).Options;
        await using var db = new SlofferDbContext(options);
        await db.Jobs.Where(j => j.Source == ArbeitnowJobSource.SourceName).ExecuteDeleteAsync();
    }
}
