using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sloffer.Jobs.Fetching;
using Sloffer.Jobs.Sources.Arbeitnow;
using Sloffer.Tests.Jobs.Jobicy;
using static Sloffer.Tests.Jobs.Arbeitnow.ArbeitnowTestSetup;

namespace Sloffer.Tests.Jobs.Arbeitnow;

/// <summary>ArbeitnowJobSource through its real DI registration, with a stubbed HTTP handler (no network).</summary>
public sealed class ArbeitnowJobSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Since = Now.AddDays(-7);
    private static readonly Dictionary<string, string?> NoKeywordFilter = new() { ["JobFetch:Arbeitnow:RequiredKeywords:0"] = "" };

    [Fact]
    public async Task FetchRecent_maps_sample_fields()
    {
        await using var services = CreateServices(StubHttpMessageHandler.Json(SampleResponse()));

        var jobs = await FetchAsync(services, Since);

        Assert.Equal(3, jobs.Count);
        var first = jobs[0];
        Assert.Equal("Arbeitnow", first.Source);
        Assert.Equal("senior-net-backend-developer-fintech-berlin-458001", first.ExternalId);
        Assert.Equal("https://www.arbeitnow.com/jobs/companies/example-payments/senior-net-backend-developer-fintech-berlin-458001", first.Url);
        Assert.Equal("Senior .NET Backend Developer (m/w/d)", first.Title);
        Assert.Equal("Example Payments GmbH", first.Company);
        Assert.Equal("Berlin", first.Location);
        Assert.Equal("Full Time, Permanent", first.EmploymentTypes);
        Assert.Null(first.Level);
        // Description is stored exactly as the source sends it.
        Assert.Equal("&lt;p&gt;Build payment services with C# and ASP.NET Core.&lt;/p&gt;", first.Description);
        // created_at 1795568115 (Unix seconds) is 2026-11-25T00:55:15Z.
        Assert.Equal(new DateTimeOffset(2026, 11, 25, 0, 55, 15, TimeSpan.Zero), first.PostedAt);
        Assert.Equal(TimeSpan.Zero, first.PostedAt.Offset);

        // remote: true with location "Remote" is not doubled; no job types gives null.
        Assert.Equal("Remote", jobs[1].Location);
        Assert.Null(jobs[1].EmploymentTypes);
        // remote: true with a city appends ", Remote".
        Assert.Equal("Munich, Remote", jobs[2].Location);
        Assert.Equal(new DateTimeOffset(2026, 11, 24, 0, 31, 17, TimeSpan.Zero), jobs[2].PostedAt);
    }

    [Theory]
    [InlineData("Berlin", false, "Berlin")]
    [InlineData("Berlin", true, "Berlin, Remote")]
    [InlineData(" Zürich ", true, "Zürich, Remote")]
    [InlineData(null, true, "Remote")]
    [InlineData("", true, "Remote")]
    [InlineData("  ", true, "Remote")]
    [InlineData("Remote", true, "Remote")]
    [InlineData("Remote job", true, "Remote job")]
    [InlineData(null, false, null)]
    [InlineData("", false, null)]
    public async Task FetchRecent_location_gets_remote_when_listing_is_remote(string? location, bool remote, string? expected)
    {
        var page = Page(1, hasNext: false, new ArbeitnowListing("a", Now.AddHours(-1), Location: location, Remote: remote));
        await using var services = CreateServices(StubHttpMessageHandler.Json(page));

        var job = Assert.Single(await FetchAsync(services, Since));

        Assert.Equal(expected, job.Location);
    }

    [Fact]
    public async Task FetchRecent_requests_page_1_with_user_agent()
    {
        var events = new List<string>();
        var handler = Pages(events, Page(1, hasNext: false, new ArbeitnowListing("a", Now)));
        await using var services = CreateServices(handler);

        await FetchAsync(services, Since);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://www.arbeitnow.com/api/job-board-api?page=1", request.RequestUri!.ToString());
        Assert.Equal(ArbeitnowServiceCollectionExtensions.UserAgent, request.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task FetchRecent_stops_after_first_page_without_jobs_in_window()
    {
        var events = new List<string>();
        var handler = Pages(events,
            Page(1, hasNext: true, new ArbeitnowListing("p1-new", Now.AddHours(-1)), new ArbeitnowListing("p1-old", Since.AddSeconds(-1))),
            Page(2, hasNext: true, new ArbeitnowListing("p2-new", Since)),
            Page(3, hasNext: true, new ArbeitnowListing("p3-old", Since.AddDays(-1))),
            Page(4, hasNext: true, new ArbeitnowListing("p4-new", Now)));
        await using var services = CreateServices(handler, NoKeywordFilter);

        var jobs = await FetchAsync(services, Since);

        // Listings exactly at the cut-off count; older ones are dropped; page 4 is never requested.
        Assert.Equal(["p1-new", "p2-new"], jobs.Select(j => j.ExternalId));
        Assert.Equal(["GET page=1", "GET page=2", "GET page=3"], events.Where(e => e.StartsWith("GET")));
    }

    [Fact]
    public async Task FetchRecent_keeps_paging_when_a_page_is_only_partly_in_window()
    {
        // Arbeitnow orders only roughly by created_at, so old listings can appear between new ones.
        var events = new List<string>();
        var handler = Pages(events,
            Page(1, hasNext: true, new ArbeitnowListing("p1-old", Since.AddDays(-2)), new ArbeitnowListing("p1-new", Now)),
            Page(2, hasNext: false, new ArbeitnowListing("p2-new", Now.AddDays(-1))));
        await using var services = CreateServices(handler, NoKeywordFilter);

        var jobs = await FetchAsync(services, Since);

        Assert.Equal(["p1-new", "p2-new"], jobs.Select(j => j.ExternalId));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(null, 5)]
    [InlineData("1", 1)]
    [InlineData("3", 3)]
    public async Task FetchRecent_stops_at_MaxPages(string? maxPages, int expectedRequests)
    {
        var events = new List<string>();
        var pages = Enumerable.Range(1, 10)
            .Select(p => Page(p, hasNext: true, new ArbeitnowListing($"job-{p}", Now.AddMinutes(-p))))
            .ToArray();
        var handler = Pages(events, pages);
        var config = new Dictionary<string, string?>(NoKeywordFilter);
        if (maxPages is not null)
            config["JobFetch:Arbeitnow:MaxPages"] = maxPages;
        await using var services = CreateServices(handler, config);

        var jobs = await FetchAsync(services, Since);

        Assert.Equal(expectedRequests, handler.Requests.Count);
        Assert.Equal(expectedRequests, jobs.Count);
    }

    [Fact]
    public async Task FetchRecent_stops_on_last_page()
    {
        var events = new List<string>();
        var handler = Pages(events,
            Page(1, hasNext: true, new ArbeitnowListing("a", Now)),
            Page(2, hasNext: false, new ArbeitnowListing("b", Now)),
            Page(3, hasNext: false, new ArbeitnowListing("c", Now)));
        await using var services = CreateServices(handler, NoKeywordFilter);

        var jobs = await FetchAsync(services, Since);

        Assert.Equal(["a", "b"], jobs.Select(j => j.ExternalId));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData("""{"data":[],"links":{"next":"https://www.arbeitnow.com/api/job-board-api?page=2"}}""")]
    [InlineData("""{"data":null}""")]
    [InlineData("""{}""")]
    public async Task FetchRecent_empty_or_missing_data_returns_nothing_and_stops(string json)
    {
        var handler = StubHttpMessageHandler.Json(json);
        await using var services = CreateServices(handler);

        var jobs = await FetchAsync(services, Since);

        Assert.Empty(jobs);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FetchRecent_waits_DelayBetweenRequests_between_pages_only()
    {
        var events = new List<string>();
        var time = new RecordingDelayTimeProvider(events);
        var handler = Pages(events,
            Page(1, hasNext: true, new ArbeitnowListing("a", Now)),
            Page(2, hasNext: true, new ArbeitnowListing("b", Now)),
            Page(3, hasNext: false, new ArbeitnowListing("c", Now)));
        await using var services = CreateServices(handler, time: time);

        await FetchAsync(services, Since);

        Assert.Equal(["GET page=1", "delay 00:00:02", "GET page=2", "delay 00:00:02", "GET page=3"], events);
    }

    [Fact]
    public async Task FetchRecent_uses_configured_delay()
    {
        var events = new List<string>();
        var time = new RecordingDelayTimeProvider(events);
        var handler = Pages(events,
            Page(1, hasNext: true, new ArbeitnowListing("a", Now)),
            Page(2, hasNext: false, new ArbeitnowListing("b", Now)));
        await using var services = CreateServices(handler,
            new Dictionary<string, string?> { ["JobFetch:Arbeitnow:DelayBetweenRequests"] = "00:00:05" }, time);

        await FetchAsync(services, Since);

        Assert.Equal([TimeSpan.FromSeconds(5)], time.Delays);
    }

    [Fact]
    public async Task FetchRecent_default_keywords_keep_only_dotnet_listings_and_log_skipped_count()
    {
        var page = Page(1, hasNext: false,
            new ArbeitnowListing("title-csharp", Now, Title: "Senior C# Developer"),
            new ArbeitnowListing("title-dotnet-lower", Now, Title: "Backend engineer (.net)"),
            new ArbeitnowListing("title-aspnet", Now, Title: "ASP.NET Core Engineer", Description: null),
            new ArbeitnowListing("tag-dotnet", Now, Title: "Backend Engineer", Tags: ["DotNet"]),
            new ArbeitnowListing("description-net", Now, Title: "Backend Engineer", Description: "<p>Our stack is .NET 10.</p>"),
            new ArbeitnowListing("objc-sharp", Now, Title: "ObjC# Developer"),
            new ArbeitnowListing("csharpish", Now, Title: "C#m musician"),
            new ArbeitnowListing("java", Now, Title: "Java Developer", Tags: ["Java"]),
            new ArbeitnowListing("sales", Now, Title: "Kundenberater"),
            new ArbeitnowListing("old-csharp", Since.AddDays(-1), Title: "C# Developer"));
        var logs = new ListLoggerProvider();
        await using var services = CreateServices(StubHttpMessageHandler.Json(page), logs: logs);

        var jobs = await FetchAsync(services, Since);

        Assert.Equal(["title-csharp", "title-dotnet-lower", "title-aspnet", "tag-dotnet", "description-net"], jobs.Select(j => j.ExternalId));
        var line = Assert.Single(logs.Messages, m => m.Contains("without a required keyword"));
        Assert.Contains("skipped 4 without a required keyword", line);
    }

    [Fact]
    public async Task FetchRecent_custom_keywords_replace_defaults()
    {
        var page = Page(1, hasNext: false,
            new ArbeitnowListing("csharp", Now, Title: "C# Developer"),
            new ArbeitnowListing("rust", Now, Title: "Rust Developer"));
        await using var services = CreateServices(StubHttpMessageHandler.Json(page),
            new Dictionary<string, string?> { ["JobFetch:Arbeitnow:RequiredKeywords:0"] = "rust" });

        var jobs = await FetchAsync(services, Since);

        Assert.Equal(["rust"], jobs.Select(j => j.ExternalId));
    }

    [Fact]
    public async Task FetchRecent_empty_keyword_list_disables_filter()
    {
        var page = Page(1, hasNext: false,
            new ArbeitnowListing("csharp", Now, Title: "C# Developer"),
            new ArbeitnowListing("sales", Now, Title: "Kundenberater", Description: null));
        await using var services = CreateServices(StubHttpMessageHandler.Json(page), NoKeywordFilter);

        var jobs = await FetchAsync(services, Since);

        Assert.Equal(["csharp", "sales"], jobs.Select(j => j.ExternalId));
    }

    [Fact]
    public async Task FetchRecent_skips_listings_with_missing_or_invalid_fields()
    {
        const string json = """
            {"data":[
              null,
              {"url":"https://www.arbeitnow.com/jobs/1","title":"No slug C#","company_name":"C","created_at":1795568115},
              {"slug":"2","title":"No url C#","company_name":"C","created_at":1795568115},
              {"slug":"3","url":"https://www.arbeitnow.com/jobs/3","title":"","company_name":"C","created_at":1795568115},
              {"slug":"4","url":"https://www.arbeitnow.com/jobs/4","title":"No company C#","created_at":1795568115},
              {"slug":"5","url":"https://www.arbeitnow.com/jobs/5","title":"No date C#","company_name":"C"},
              {"slug":"6","url":"https://www.arbeitnow.com/jobs/6","title":"Huge date C#","company_name":"C","created_at":999999999999999},
              {"slug":"7","url":"https://www.arbeitnow.com/jobs/7","title":"Minimal C#","company_name":"C","created_at":1795568115}
            ]}
            """;
        await using var services = CreateServices(StubHttpMessageHandler.Json(json));

        var jobs = await FetchAsync(services, Since);

        var job = Assert.Single(jobs);
        Assert.Equal("7", job.ExternalId);
        Assert.Null(job.Location);
        Assert.Null(job.EmploymentTypes);
        Assert.Null(job.Description);
    }

    [Theory]
    [InlineData("""{"data":"not an array"}""")]
    [InlineData("""{"data":[{"created_at":"yesterday"}]}""")]
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
    public async Task FetchRecent_non_success_status_throws_HttpRequestException_without_retry(HttpStatusCode status)
    {
        var handler = StubHttpMessageHandler.Status(status);
        await using var services = CreateServices(handler);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => FetchAsync(services, Since));

        Assert.Equal(status, ex.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FetchRecent_failure_on_a_later_page_throws()
    {
        var page1 = Page(1, hasNext: true, new ArbeitnowListing("a", Now));
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.Query.EndsWith("page=1", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(page1) }
            : new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("") });
        await using var services = CreateServices(handler);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => FetchAsync(services, Since));

        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public void Options_defaults_match_spec()
    {
        var options = new ArbeitnowOptions();

        Assert.Equal(5, options.MaxPages);
        Assert.Equal(TimeSpan.FromSeconds(2), options.DelayBetweenRequests);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Timeout);
        Assert.Equal([".NET", "C#", "dotnet", "ASP.NET"], options.EffectiveRequiredKeywords);
    }

    [Theory]
    [InlineData("""{"JobFetch":{"Arbeitnow":{}}}""", new[] { ".NET", "C#", "dotnet", "ASP.NET" })]
    [InlineData("""{"JobFetch":{"Arbeitnow":{"RequiredKeywords":[]}}}""", new string[0])]
    [InlineData("""{"JobFetch":{"Arbeitnow":{"RequiredKeywords":[""]}}}""", new string[0])]
    [InlineData("""{"JobFetch":{"Arbeitnow":{"RequiredKeywords":["Rust"," Go "]}}}""", new[] { "Rust", "Go" })]
    public void Options_RequiredKeywords_bind_from_json(string json, string[] expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
            .Build();
        var services = new ServiceCollection().AddLogging();
        services.AddArbeitnowSource(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ArbeitnowOptions>>().Value;

        Assert.Equal(expected, options.EffectiveRequiredKeywords);
    }

    [Theory]
    [InlineData(null, 60)]
    [InlineData("00:30:00", 30)]
    [InlineData("02:00:00", 120)]
    public void Registration_fetch_interval_comes_from_JobFetch_options(string? sourceOverride, int expectedMinutes)
    {
        var config = new Dictionary<string, string?>();
        if (sourceOverride is not null)
            config["JobFetch:Sources:Arbeitnow:MinInterval"] = sourceOverride;
        using var services = CreateServices(StubHttpMessageHandler.Json("{}"), config);

        var fetch = services.GetRequiredService<IOptionsMonitor<JobFetchOptions>>().CurrentValue;

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), fetch.GetMinInterval(ArbeitnowJobSource.SourceName));
    }

    [Theory]
    [InlineData("JobFetch:Arbeitnow:MaxPages", "0")]
    [InlineData("JobFetch:Arbeitnow:MaxPages", "21")]
    [InlineData("JobFetch:Arbeitnow:DelayBetweenRequests", "-00:00:01")]
    [InlineData("JobFetch:Arbeitnow:DelayBetweenRequests", "00:01:01")]
    [InlineData("JobFetch:Arbeitnow:Timeout", "00:00:00")]
    public void Options_invalid_values_are_rejected(string key, string value)
    {
        using var services = CreateServices(StubHttpMessageHandler.Json("{}"), new Dictionary<string, string?> { [key] = value });

        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<ArbeitnowOptions>>().Value);
    }
}
