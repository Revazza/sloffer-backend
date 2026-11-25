using System.Net;
using System.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sloffer.Jobs.Fetching;
using Sloffer.Jobs.Sources.Jobicy;
using Sloffer.Tests.Jobs.Arbeitnow;
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
    public async Task FetchRecent_sends_one_request_per_default_query_in_order_with_user_agent()
    {
        var handler = StubHttpMessageHandler.Json(SampleResponse());
        await using var services = CreateServices(handler);

        await FetchAsync(services, Since);

        Assert.Equal(
        [
            "https://jobicy.com/api/v2/remote-jobs?count=100&geo=europe&industry=engineering",
            "https://jobicy.com/api/v2/remote-jobs?count=100&geo=europe&tag=.net",
            "https://jobicy.com/api/v2/remote-jobs?count=100&geo=anywhere&tag=.net",
        ], handler.Requests.Select(r => r.RequestUri!.ToString()));
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(JobicyServiceCollectionExtensions.UserAgent, request.Headers.UserAgent.ToString());
        });
    }

    [Fact]
    public async Task FetchRecent_uses_configured_queries_and_omits_empty_filters()
    {
        var handler = StubHttpMessageHandler.Json(SampleResponse());
        await using var services = CreateServices(handler, new Dictionary<string, string?>
        {
            ["JobFetch:Jobicy:Count"] = "5",
            ["JobFetch:Jobicy:Queries:0:Geo"] = "usa",
            ["JobFetch:Jobicy:Queries:0:Industry"] = "",
            ["JobFetch:Jobicy:Queries:1:Tag"] = " c# developer ",
            ["JobFetch:Jobicy:Queries:2:Industry"] = "engineering",
            ["JobFetch:Jobicy:Queries:2:Tag"] = ".net",
        });

        await FetchAsync(services, Since);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("https://jobicy.com/api/v2/remote-jobs?count=5&geo=usa", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Equal("https://jobicy.com/api/v2/remote-jobs?count=5&industry=engineering&tag=.net", handler.Requests[2].RequestUri!.AbsoluteUri);

        // Tags are trimmed and URL-encoded (`#` must not end the URL).
        var tagged = HttpUtility.ParseQueryString(handler.Requests[1].RequestUri!.Query);
        Assert.Equal(new string?[] { "count", "tag" }, tagged.AllKeys);
        Assert.Equal("5", tagged["count"]);
        Assert.Equal("c# developer", tagged["tag"]);
    }

    [Fact]
    public async Task FetchRecent_merges_queries_and_drops_duplicate_ids_keeping_the_first()
    {
        var posted = Since.AddHours(1);
        var handler = ByQuery(query => JsonResponse(query switch
        {
            "geo=europe&industry=engineering" => Listings(posted, 1, 2),
            "geo=europe&tag=.net" => Listings(posted, 2, 3, 3),
            "geo=anywhere&tag=.net" => Listings(posted, 4, 1),
            _ => throw new InvalidOperationException($"Unexpected query {query}"),
        }));
        await using var services = CreateServices(handler);

        var jobs = await FetchAsync(services, Since);

        Assert.Equal(["1", "2", "3", "4"], jobs.Select(j => j.ExternalId));
        Assert.Equal("https://jobicy.com/jobs/1-dotnet-developer", jobs[0].Url);
    }

    [Fact]
    public async Task FetchRecent_applies_since_to_every_query()
    {
        var handler = ByQuery(query => JsonResponse(query switch
        {
            "geo=europe&industry=engineering" => Listings(Since.AddSeconds(-1), 1),
            "geo=europe&tag=.net" => Listings(Since, 2),
            _ => Listings(Since.AddDays(-3), 3),
        }));
        await using var services = CreateServices(handler);

        var jobs = await FetchAsync(services, Since);

        Assert.Equal(["2"], jobs.Select(j => j.ExternalId));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(null)] // malformed body
    public async Task FetchRecent_one_failing_query_keeps_the_others(HttpStatusCode? failure)
    {
        var posted = Since.AddHours(1);
        var logs = new ListLoggerProvider();
        var handler = ByQuery(query => query switch
        {
            "geo=europe&tag=.net" => failure is { } status
                ? new HttpResponseMessage(status) { Content = new StringContent("") }
                : JsonResponse("<html>oops</html>"),
            "geo=europe&industry=engineering" => JsonResponse(Listings(posted, 1)),
            _ => JsonResponse(Listings(posted, 3)),
        });
        await using var services = CreateServices(handler, logs: logs);

        var jobs = await FetchAsync(services, Since);

        Assert.Equal(["1", "3"], jobs.Select(j => j.ExternalId));
        Assert.Equal(3, handler.Requests.Count); // no retry
        Assert.Contains(logs.Messages, m => m.Contains("geo=europe&tag=.net failed"));
        Assert.Contains(logs.Messages, m => m.Contains("ran 3 queries (1 failed)"));
    }

    [Fact]
    public async Task FetchRecent_one_query_throwing_any_exception_keeps_the_others()
    {
        var handler = ByQuery(query => query == "geo=anywhere&tag=.net"
            ? throw new TaskCanceledException("HTTP timeout")
            : JsonResponse(Listings(Since, query.Contains("industry") ? 1 : 2)));
        await using var services = CreateServices(handler);

        var jobs = await FetchAsync(services, Since);

        Assert.Equal(["1", "2"], jobs.Select(j => j.ExternalId));
    }

    [Fact]
    public async Task FetchRecent_all_queries_failing_rethrows_without_retry()
    {
        var handler = ByQuery(query => query.Contains("industry")
            ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("") }
            : new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("") });
        await using var services = CreateServices(handler);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => FetchAsync(services, Since));

        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task FetchRecent_all_queries_returning_nothing_is_not_a_failure()
    {
        var handler = StubHttpMessageHandler.Json("""{"jobs":[]}""");
        await using var services = CreateServices(handler);

        Assert.Empty(await FetchAsync(services, Since));
    }

    [Fact]
    public async Task FetchRecent_cancellation_is_not_swallowed()
    {
        using var cts = new CancellationTokenSource();
        var handler = ByQuery(_ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        await using var services = CreateServices(handler);
        await using var scope = services.CreateAsyncScope();
        var source = Assert.Single(scope.ServiceProvider.GetServices<IJobSource>());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.FetchRecentAsync(Since, cts.Token));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FetchRecent_waits_DelayBetweenRequests_between_queries_only()
    {
        var events = new List<string>();
        var time = new RecordingDelayTimeProvider(events);
        var handler = ByQuery(
            query => query.Contains("industry")
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("") }
                : JsonResponse(Listings(Since, 1)),
            events);
        await using var services = CreateServices(handler, time: time);

        await FetchAsync(services, Since);

        // A failed query still counts as a request: the next one waits too.
        Assert.Equal(
        [
            "GET geo=europe&industry=engineering",
            "delay 00:00:03",
            "GET geo=europe&tag=.net",
            "delay 00:00:03",
            "GET geo=anywhere&tag=.net",
        ], events);
    }

    [Fact]
    public async Task FetchRecent_uses_configured_delay_and_zero_disables_it()
    {
        var time = new RecordingDelayTimeProvider([]);
        await using (var services = CreateServices(StubHttpMessageHandler.Json("{}"),
            new Dictionary<string, string?> { ["JobFetch:Jobicy:DelayBetweenRequests"] = "00:00:10" }, time))
        {
            await FetchAsync(services, Since);
        }

        Assert.Equal([TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)], time.Delays);

        var none = new RecordingDelayTimeProvider([]);
        await using (var services = CreateServices(StubHttpMessageHandler.Json("{}"),
            new Dictionary<string, string?> { ["JobFetch:Jobicy:DelayBetweenRequests"] = "00:00:00" }, none))
        {
            await FetchAsync(services, Since);
        }

        Assert.Empty(none.Delays);
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
        Assert.Equal(3, handler.Requests.Count); // one per query, no retry
    }

    [Fact]
    public void Options_defaults_match_spec()
    {
        var options = new JobicyOptions();

        Assert.Equal(100, options.Count);
        Assert.Null(options.Queries);
        Assert.Equal(
            [("europe", "engineering", null), ("europe", null, ".net"), ("anywhere", null, ".net")],
            options.EffectiveQueries.Select(q => (q.Geo, q.Industry, q.Tag)));
        Assert.Equal(TimeSpan.FromSeconds(3), options.DelayBetweenRequests);
        Assert.Equal(TimeSpan.FromMinutes(60), options.MinInterval);
        Assert.True(options.Timeout > TimeSpan.Zero);
    }

    [Fact]
    public void Options_configured_queries_replace_the_defaults()
    {
        using var services = CreateServices(StubHttpMessageHandler.Json("{}"),
            new Dictionary<string, string?> { ["JobFetch:Jobicy:Queries:0:Tag"] = ".net" });

        var options = services.GetRequiredService<IOptions<JobicyOptions>>().Value;

        var query = Assert.Single(options.EffectiveQueries);
        Assert.Equal((null, null, ".net"), (query.Geo, query.Industry, query.Tag));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData(".net")]
    [InlineData("12345678901234567890123456789012345678901234567890")] // 50
    public void Options_tag_of_3_to_50_characters_is_accepted(string tag)
    {
        using var services = CreateServices(StubHttpMessageHandler.Json("{}"),
            new Dictionary<string, string?> { ["JobFetch:Jobicy:Queries:0:Tag"] = tag });

        Assert.Equal(tag, Assert.Single(services.GetRequiredService<IOptions<JobicyOptions>>().Value.EffectiveQueries).Tag);
    }

    [Fact]
    public void Options_five_queries_are_accepted()
    {
        var config = Enumerable.Range(0, 5).ToDictionary(i => $"JobFetch:Jobicy:Queries:{i}:Geo", i => (string?)$"geo{i}");
        using var services = CreateServices(StubHttpMessageHandler.Json("{}"), config);

        Assert.Equal(5, services.GetRequiredService<IOptions<JobicyOptions>>().Value.EffectiveQueries.Count);
    }

    [Fact]
    public void Options_more_than_five_queries_are_rejected()
    {
        var config = Enumerable.Range(0, 6).ToDictionary(i => $"JobFetch:Jobicy:Queries:{i}:Geo", i => (string?)$"geo{i}");
        using var services = CreateServices(StubHttpMessageHandler.Json("{}"), config);

        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<JobicyOptions>>().Value);
    }

    [Theory]
    [InlineData("JobFetch:Jobicy:Queries:0:Tag", "ab")]
    [InlineData("JobFetch:Jobicy:Queries:0:Tag", " ab ")]
    [InlineData("JobFetch:Jobicy:Queries:0:Tag", "123456789012345678901234567890123456789012345678901")] // 51
    [InlineData("JobFetch:Jobicy:Queries:0:Geo", "")]
    [InlineData("JobFetch:Jobicy:Queries:0:Industry", " ")]
    public void Options_invalid_queries_are_rejected(string key, string value)
    {
        using var services = CreateServices(StubHttpMessageHandler.Json("{}"), new Dictionary<string, string?> { [key] = value });

        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<JobicyOptions>>().Value);
    }

    [Fact]
    public void Options_query_without_geo_industry_or_tag_is_rejected()
    {
        using var services = CreateServices(StubHttpMessageHandler.Json("{}"), new Dictionary<string, string?>
        {
            ["JobFetch:Jobicy:Queries:0:Geo"] = "europe",
            ["JobFetch:Jobicy:Queries:1:Geo"] = "",
            ["JobFetch:Jobicy:Queries:1:Industry"] = "",
            ["JobFetch:Jobicy:Queries:1:Tag"] = "",
        });

        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<JobicyOptions>>().Value);
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
    [InlineData("JobFetch:Jobicy:DelayBetweenRequests", "-00:00:01")]
    [InlineData("JobFetch:Jobicy:DelayBetweenRequests", "00:01:01")]
    public void Options_invalid_values_are_rejected(string key, string value)
    {
        using var services = CreateServices(StubHttpMessageHandler.Json("{}"), new Dictionary<string, string?> { [key] = value });

        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<JobicyOptions>>().Value);
    }
}
