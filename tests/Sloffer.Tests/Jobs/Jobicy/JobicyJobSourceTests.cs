using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sloffer.Jobs.Fetching;
using Sloffer.Jobs.Sources.Jobicy;
using static Sloffer.Tests.Jobs.Jobicy.JobicyTestSetup;

namespace Sloffer.Tests.Jobs.Jobicy;

/// <summary>JobicyJobSource through its real DI registration, with a stubbed HTTP handler (no network).</summary>
public sealed class JobicyJobSourceTests
{
    private static readonly DateTimeOffset Since = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FetchRecent_maps_sample_fields()
    {
        await using var services = CreateServices(StubHttpMessageHandler.Json(SampleResponse()));

        var jobs = await FetchAsync(services, Since);

        Assert.Equal(2, jobs.Count);
        var first = jobs[0];
        Assert.Equal("jobicy", first.Source);
        Assert.Equal("164017", first.ExternalId);
        Assert.Equal("https://jobicy.com/jobs/164017-mulesoft-development-engineer", first.Url);
        Assert.Equal("MuleSoft Development Engineer", first.Title);
        Assert.Equal("Veeam Software", first.Company);
        Assert.Equal("Czechia", first.Location);
        Assert.Equal("Full-Time", first.EmploymentTypes);
        Assert.Equal("Entry-Level, Junior", first.Level);
        Assert.StartsWith("<p>Veeam is the Data and AI Trust Company.</p>", first.Description);
        Assert.Equal(new DateTimeOffset(2026, 11, 23, 18, 30, 24, TimeSpan.Zero), first.PostedAt);

        var second = jobs[1];
        Assert.Equal("163960", second.ExternalId);
        Assert.Equal("Full-Time, Contract", second.EmploymentTypes);
        // 14:49:31+02:00 is converted to UTC.
        Assert.Equal(new DateTimeOffset(2026, 11, 23, 12, 49, 31, TimeSpan.Zero), second.PostedAt);
        Assert.Equal(TimeSpan.Zero, second.PostedAt.Offset);
    }

    [Fact]
    public async Task FetchRecent_returns_only_jobs_posted_since()
    {
        await using var services = CreateServices(StubHttpMessageHandler.Json(SampleResponse()));
        var secondPostedAt = new DateTimeOffset(2026, 11, 23, 12, 49, 31, TimeSpan.Zero);

        var atEdge = await FetchAsync(services, secondPostedAt);
        var afterEdge = await FetchAsync(services, secondPostedAt.AddSeconds(1));
        var everything = await FetchAsync(services, DateTimeOffset.MinValue);

        Assert.Equal(["164017", "163960"], atEdge.Select(j => j.ExternalId));
        Assert.Equal(["164017"], afterEdge.Select(j => j.ExternalId));
        Assert.Equal(["164017", "163960", "160001"], everything.Select(j => j.ExternalId));
    }

    [Fact]
    public async Task FetchRecent_sends_exactly_one_request_with_default_query_and_user_agent()
    {
        var handler = StubHttpMessageHandler.Json(SampleResponse());
        await using var services = CreateServices(handler);

        await FetchAsync(services, Since);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://jobicy.com/api/v2/remote-jobs?count=100&geo=europe&industry=engineering", request.RequestUri!.ToString());
        Assert.Equal(JobicyServiceCollectionExtensions.UserAgent, request.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task FetchRecent_uses_configured_query_and_omits_empty_filters()
    {
        var handler = StubHttpMessageHandler.Json(SampleResponse());
        await using var services = CreateServices(handler, new Dictionary<string, string?>
        {
            ["JobFetch:Jobicy:Count"] = "5",
            ["JobFetch:Jobicy:Geo"] = "usa",
            ["JobFetch:Jobicy:Industry"] = "",
        });

        await FetchAsync(services, Since);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://jobicy.com/api/v2/remote-jobs?count=5&geo=usa", request.RequestUri!.ToString());
    }

    [Theory]
    [InlineData("""{"jobs":[],"jobCount":0,"success":true}""")]
    [InlineData("""{"jobs":null}""")]
    [InlineData("""{"success":true,"statusCode":200}""")]
    [InlineData("""{}""")]
    public async Task FetchRecent_empty_or_missing_jobs_returns_nothing(string json)
    {
        await using var services = CreateServices(StubHttpMessageHandler.Json(json));

        var jobs = await FetchAsync(services, Since);

        Assert.Empty(jobs);
    }

    [Fact]
    public async Task FetchRecent_skips_listings_with_missing_or_invalid_fields()
    {
        const string json = """
            {"jobs":[
              null,
              {"url":"https://jobicy.com/jobs/1","jobTitle":"No id","companyName":"C","pubDate":"2026-11-24T10:00:00+00:00"},
              {"id":2,"jobTitle":"No url","companyName":"C","pubDate":"2026-11-24T10:00:00+00:00"},
              {"id":3,"url":"https://jobicy.com/jobs/3","jobTitle":"","companyName":"C","pubDate":"2026-11-24T10:00:00+00:00"},
              {"id":4,"url":"https://jobicy.com/jobs/4","jobTitle":"No company","pubDate":"2026-11-24T10:00:00+00:00"},
              {"id":5,"url":"https://jobicy.com/jobs/5","jobTitle":"Bad date","companyName":"C","pubDate":"yesterday"},
              {"id":6,"url":"https://jobicy.com/jobs/6","jobTitle":"No date","companyName":"C"},
              {"id":7,"url":"https://jobicy.com/jobs/7","jobTitle":"Minimal","companyName":"C","pubDate":"2026-11-24T10:00:00+00:00"}
            ]}
            """;
        await using var services = CreateServices(StubHttpMessageHandler.Json(json));

        var jobs = await FetchAsync(services, Since);

        var job = Assert.Single(jobs);
        Assert.Equal("7", job.ExternalId);
        Assert.Null(job.Location);
        Assert.Null(job.EmploymentTypes);
        Assert.Null(job.Level);
        Assert.Null(job.Description);
    }

    [Theory]
    [InlineData("""{"jobs":"not an array"}""")]
    [InlineData("""{"jobs":[{"id":"abc"}]}""")]
    [InlineData("<html>Service unavailable</html>")]
    public async Task FetchRecent_malformed_body_throws(string body)
    {
        await using var services = CreateServices(StubHttpMessageHandler.Json(body));

        await Assert.ThrowsAnyAsync<Exception>(() => FetchAsync(services, Since));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task FetchRecent_non_success_status_throws_HttpRequestException_with_status(HttpStatusCode status)
    {
        var handler = StubHttpMessageHandler.Status(status);
        await using var services = CreateServices(handler);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => FetchAsync(services, Since));

        Assert.Equal(status, ex.StatusCode);
        Assert.Single(handler.Requests); // no retry
    }

    [Fact]
    public void Options_defaults_match_spec()
    {
        var options = new JobicyOptions();

        Assert.Equal(100, options.Count);
        Assert.Equal("europe", options.Geo);
        Assert.Equal("engineering", options.Industry);
        Assert.Equal(TimeSpan.FromMinutes(60), options.MinInterval);
        Assert.True(options.Timeout > TimeSpan.Zero);
    }

    [Theory]
    [InlineData(null, null, 60)]
    [InlineData("00:10:00", null, 60)]
    [InlineData("00:00:00", null, 60)]
    [InlineData("03:00:00", null, 180)]
    [InlineData(null, "02:00:00", 120)]
    [InlineData("00:10:00", "02:00:00", 120)]
    public void Registration_fetch_interval_is_never_below_one_hour(string? sourceOverride, string? jobicyInterval, int expectedMinutes)
    {
        var config = new Dictionary<string, string?> { ["JobFetch:MinInterval"] = "00:01:00" };
        if (sourceOverride is not null)
            config["JobFetch:Sources:Jobicy:MinInterval"] = sourceOverride;
        if (jobicyInterval is not null)
            config["JobFetch:Jobicy:MinInterval"] = jobicyInterval;
        using var services = CreateServices(StubHttpMessageHandler.Json("{}"), config);

        var fetch = services.GetRequiredService<IOptionsMonitor<JobFetchOptions>>().CurrentValue;

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), fetch.GetMinInterval(JobicyJobSource.SourceName));
    }

    [Theory]
    [InlineData("JobFetch:Jobicy:MinInterval", "00:59:59")]
    [InlineData("JobFetch:Jobicy:Count", "0")]
    [InlineData("JobFetch:Jobicy:Count", "201")]
    [InlineData("JobFetch:Jobicy:Timeout", "00:00:00")]
    public void Options_invalid_values_are_rejected(string key, string value)
    {
        using var services = CreateServices(StubHttpMessageHandler.Json("{}"), new Dictionary<string, string?> { [key] = value });

        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<JobicyOptions>>().Value);
    }
}
