using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sloffer.Jobs.Fetching;
using Sloffer.Jobs.Sources.RemoteOk;
using Sloffer.Tests.Jobs.Arbeitnow;
using Sloffer.Tests.Jobs.Jobicy;

namespace Sloffer.Tests.Jobs.RemoteOk;

/// <summary>Remote OK source against a fake HTTP layer (no real network).</summary>
public sealed class RemoteOkJobSourceTests
{
    private static readonly DateTimeOffset Since = new(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Fetch_skips_the_legal_notice_and_maps_dotnet_listings()
    {
        await using var services = CreateServices(StubHttpMessageHandler.Json(Fixture()));

        var jobs = await FetchAsync(services, Since);

        var job = Assert.Single(jobs);
        Assert.Equal("Remote OK", job.Source);
        Assert.Equal("1130001", job.ExternalId);
        Assert.Equal("https://remoteOK.com/remote-jobs/remote-senior-net-developer-example-payments-1130001", job.Url);
        Assert.Equal("Senior .NET Developer", job.Title);
        Assert.Equal("Example Payments", job.Company);
        Assert.Equal("Europe", job.Location);
        Assert.Null(job.EmploymentTypes);
        Assert.Equal("<p>Build payment APIs in C# and ASP.NET Core.</p>", job.Description);
        Assert.Equal(new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.Zero), job.PostedAt);
    }

    [Fact]
    public async Task Fetch_sends_one_request_with_a_user_agent()
    {
        var handler = StubHttpMessageHandler.Json(Fixture());
        await using var services = CreateServices(handler);

        await FetchAsync(services, Since);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://remoteok.com/api", request.RequestUri!.ToString());
        Assert.Contains("Sloffer", request.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task Fetch_with_empty_keyword_list_keeps_every_valid_listing_in_the_window()
    {
        await using var services = CreateServices(
            StubHttpMessageHandler.Json(Fixture()),
            new Dictionary<string, string?> { ["JobFetch:RemoteOk:RequiredKeywords:0"] = "" });

        var jobs = await FetchAsync(services, Since);

        Assert.Equal(["1130001", "1130002"], jobs.Select(j => j.ExternalId).Order());
        Assert.Null(jobs.Single(j => j.ExternalId == "1130002").Location);
    }

    [Fact]
    public async Task Fetch_drops_listings_older_than_the_window()
    {
        await using var services = CreateServices(StubHttpMessageHandler.Json(Fixture()));

        var jobs = await FetchAsync(services, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(["1130001", "1130003"], jobs.Select(j => j.ExternalId).Order());
    }

    [Fact]
    public async Task Fetch_logs_one_summary_line_and_counts_invalid_listings_but_not_the_legal_notice()
    {
        var logs = new ListLoggerProvider();
        await using var services = CreateServices(StubHttpMessageHandler.Json(Fixture()), logs: logs);

        await FetchAsync(services, Since);

        Assert.Contains(logs.Messages, m => m.Contains("Remote OK: 4 listings, 1 older than the window, skipped 1 without a required keyword, kept 1."));
        Assert.Contains(logs.Messages, m => m.Contains("Remote OK: skipped 1 of 4 listings with missing or invalid fields."));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Fetch_http_error_throws_with_status_code(HttpStatusCode status)
    {
        await using var services = CreateServices(StubHttpMessageHandler.Status(status));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => FetchAsync(services, Since));

        Assert.Equal(status, error.StatusCode);
    }

    [Fact]
    public async Task Fetch_empty_array_returns_no_jobs()
    {
        await using var services = CreateServices(StubHttpMessageHandler.Json("[]"));

        Assert.Empty(await FetchAsync(services, Since));
    }

    [Fact]
    public void Invalid_options_fail_validation()
    {
        using var services = CreateServices(
            StubHttpMessageHandler.Json("[]"),
            new Dictionary<string, string?> { ["JobFetch:RemoteOk:Timeout"] = "00:00:00" });

        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<RemoteOkOptions>>().Value);
    }

    private static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Jobs", "RemoteOk", "Fixtures", "remoteok-api.json"));

    private static ServiceProvider CreateServices(
        HttpMessageHandler handler,
        IDictionary<string, string?>? config = null,
        ListLoggerProvider? logs = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config ?? new Dictionary<string, string?>()).Build();
        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            if (logs is not null)
                b.AddProvider(logs);
        });
        services.AddJobFetching(configuration);
        services.AddRemoteOkSource(configuration).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task<IReadOnlyList<FetchedJob>> FetchAsync(ServiceProvider services, DateTimeOffset since)
    {
        await using var scope = services.CreateAsyncScope();
        var source = Assert.Single(scope.ServiceProvider.GetServices<IJobSource>());
        return await source.FetchRecentAsync(since, CancellationToken.None);
    }
}
