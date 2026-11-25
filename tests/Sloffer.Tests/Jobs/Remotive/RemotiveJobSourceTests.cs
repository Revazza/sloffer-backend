using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sloffer.Jobs.Fetching;
using Sloffer.Jobs.Sources.Remotive;
using Sloffer.Tests.Jobs.Arbeitnow;
using Sloffer.Tests.Jobs.Jobicy;

namespace Sloffer.Tests.Jobs.Remotive;

/// <summary>Remotive source against a fake HTTP layer (no real network).</summary>
public sealed class RemotiveJobSourceTests
{
    private static readonly DateTimeOffset Since = new(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Fetch_maps_dotnet_listings_and_converts_publication_date_to_utc()
    {
        await using var services = CreateServices(StubHttpMessageHandler.Json(Fixture()));

        var jobs = await FetchAsync(services, Since);

        var job = Assert.Single(jobs);
        Assert.Equal("Remotive", job.Source);
        Assert.Equal("2090001", job.ExternalId);
        Assert.Equal("https://remotive.com/remote-jobs/software-dev/senior-net-engineer-2090001", job.Url);
        Assert.Equal("Senior .NET Engineer", job.Title);
        Assert.Equal("Example Payments", job.Company);
        Assert.Equal("Europe", job.Location);
        Assert.Equal("Full-Time", job.EmploymentTypes);
        Assert.Null(job.Level);
        Assert.Equal("<p>Build payment APIs in C# and ASP.NET Core.</p>", job.Description);
        Assert.Equal(new DateTimeOffset(2026, 11, 24, 9, 30, 0, TimeSpan.Zero), job.PostedAt);
    }

    [Fact]
    public async Task Fetch_requests_the_configured_category_once()
    {
        var handler = StubHttpMessageHandler.Json(Fixture());
        await using var services = CreateServices(handler, new Dictionary<string, string?> { ["JobFetch:Remotive:Category"] = "devops" });

        await FetchAsync(services, Since);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://remotive.com/api/remote-jobs?category=devops", request.RequestUri!.ToString());
        Assert.Contains("Sloffer", request.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task Fetch_with_empty_keyword_list_keeps_every_valid_listing_in_the_window()
    {
        await using var services = CreateServices(
            StubHttpMessageHandler.Json(Fixture()),
            new Dictionary<string, string?> { ["JobFetch:Remotive:RequiredKeywords:0"] = "" });

        var jobs = await FetchAsync(services, Since);

        Assert.Equal(["2090001", "2090002"], jobs.Select(j => j.ExternalId).Order());
    }

    [Fact]
    public async Task Fetch_drops_listings_older_than_the_window()
    {
        await using var services = CreateServices(StubHttpMessageHandler.Json(Fixture()));

        var jobs = await FetchAsync(services, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(["2090001", "2090003"], jobs.Select(j => j.ExternalId).Order());
    }

    [Fact]
    public async Task Fetch_logs_one_summary_line()
    {
        var logs = new ListLoggerProvider();
        await using var services = CreateServices(StubHttpMessageHandler.Json(Fixture()), logs: logs);

        await FetchAsync(services, Since);

        Assert.Contains(logs.Messages, m => m.Contains("Remotive: 4 listings in software-dev, 1 older than the window, skipped 1 without a required keyword, kept 1."));
        Assert.Contains(logs.Messages, m => m.Contains("Remotive: skipped 1 of 4 listings with missing or invalid fields."));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Fetch_http_error_throws_with_status_code(HttpStatusCode status)
    {
        await using var services = CreateServices(StubHttpMessageHandler.Status(status));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => FetchAsync(services, Since));

        Assert.Equal(status, error.StatusCode);
    }

    [Fact]
    public async Task Fetch_empty_response_returns_no_jobs()
    {
        await using var services = CreateServices(StubHttpMessageHandler.Json("{}"));

        Assert.Empty(await FetchAsync(services, Since));
    }

    [Theory]
    [InlineData("full_time", "Full-Time")]
    [InlineData("contract", "Contract")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void EmploymentType_formats_remotive_job_type(string? jobType, string? expected)
    {
        Assert.Equal(expected, RemotiveJobSource.EmploymentType(jobType));
    }

    [Theory]
    [InlineData("JobFetch:Remotive:Category", "")]
    [InlineData("JobFetch:Remotive:Timeout", "00:00:00")]
    public void Invalid_options_fail_validation(string key, string value)
    {
        using var services = CreateServices(StubHttpMessageHandler.Json("{}"), new Dictionary<string, string?> { [key] = value });

        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<RemotiveOptions>>().Value);
    }

    private static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Jobs", "Remotive", "Fixtures", "remotive-software-dev.json"));

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
        services.AddRemotiveSource(configuration).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task<IReadOnlyList<FetchedJob>> FetchAsync(ServiceProvider services, DateTimeOffset since)
    {
        await using var scope = services.CreateAsyncScope();
        var source = Assert.Single(scope.ServiceProvider.GetServices<IJobSource>());
        return await source.FetchRecentAsync(since, CancellationToken.None);
    }
}
