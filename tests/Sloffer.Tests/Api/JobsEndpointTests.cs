using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Sloffer.Api.Contracts;
using Sloffer.Data;

namespace Sloffer.Tests.Api;

/// <summary>
/// GET /api/jobs and GET /api/jobs/{id} against the real test database.
/// Each test seeds jobs under its own unique source and filters by it, so tests never see each other's rows.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class JobsEndpointTests(SlofferApiFactory factory)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task List_returns_jobs_newest_first_then_by_id()
    {
        var source = UniqueSource();
        var tie = Now.AddHours(-3);
        var tieA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
        var tieB = Guid.Parse("00000000-0000-0000-0000-00000000000b");
        await SeedAsync(
            NewJob(source, "old", Now.AddHours(-10)),
            NewJob(source, "tie-b", tie, id: tieB),
            NewJob(source, "newest", Now.AddMinutes(-5)),
            NewJob(source, "tie-a", tie, id: tieA));

        var body = await ListAsync($"source={source}");

        Assert.Equal(["newest", "tie-a", "tie-b", "old"], body.Items.Select(j => j.Title));
    }

    [Fact]
    public async Task List_pages_results_and_reports_total_count()
    {
        var source = UniqueSource();
        await SeedAsync(Enumerable.Range(1, 5).Select(i => NewJob(source, $"job-{i}", Now.AddMinutes(-i))).ToArray());

        var page1 = await ListAsync($"source={source}&page=1&pageSize=2");
        var page3 = await ListAsync($"source={source}&page=3&pageSize=2");
        var page4 = await ListAsync($"source={source}&page=4&pageSize=2");

        Assert.Equal(["job-1", "job-2"], page1.Items.Select(j => j.Title));
        Assert.Equal((1, 2, 5), (page1.Page, page1.PageSize, page1.TotalCount));
        Assert.Equal(["job-5"], page3.Items.Select(j => j.Title));
        Assert.Empty(page4.Items);
        Assert.Equal(5, page4.TotalCount);
    }

    [Fact]
    public async Task List_without_paging_uses_page_1_and_page_size_20()
    {
        var source = UniqueSource();
        await SeedAsync(Enumerable.Range(1, 21).Select(i => NewJob(source, $"job-{i}", Now.AddMinutes(-i))).ToArray());

        var body = await ListAsync($"source={source}");

        Assert.Equal((1, 20, 21), (body.Page, body.PageSize, body.TotalCount));
        Assert.Equal(20, body.Items.Count);
    }

    [Fact]
    public async Task List_q_matches_title_or_company_case_insensitively()
    {
        var source = UniqueSource();
        await SeedAsync(
            NewJob(source, "Senior .NET Developer", Now.AddHours(-1), company: "Acme"),
            NewJob(source, "Backend Engineer", Now.AddHours(-2), company: "DotNet Shop"),
            NewJob(source, "Frontend Engineer", Now.AddHours(-3), company: "Acme"));

        var byTitle = await ListAsync($"source={source}&q=.net%20DEVELOPER");
        var byTitleOrCompany = await ListAsync($"source={source}&q=dotnet");
        var byCompany = await ListAsync($"source={source}&q=acme");

        Assert.Equal(["Senior .NET Developer"], byTitle.Items.Select(j => j.Title));
        Assert.Equal(["Backend Engineer"], byTitleOrCompany.Items.Select(j => j.Title));
        Assert.Equal(["Senior .NET Developer", "Frontend Engineer"], byCompany.Items.Select(j => j.Title));
        Assert.Equal(2, byCompany.TotalCount);
    }

    [Fact]
    public async Task List_location_filter_matches_contains_case_insensitively()
    {
        var source = UniqueSource();
        await SeedAsync(
            NewJob(source, "uk", Now.AddHours(-1), location: "London, UK"),
            NewJob(source, "spain", Now.AddHours(-2), location: "Spain"),
            NewJob(source, "none", Now.AddHours(-3), location: null));

        var body = await ListAsync($"source={source}&location=london");

        Assert.Equal(["uk"], body.Items.Select(j => j.Title));
        Assert.Equal(1, body.TotalCount);
    }

    [Fact]
    public async Task List_source_filter_is_exact()
    {
        var source = UniqueSource();
        await SeedAsync(NewJob(source, "exact", Now.AddHours(-1)), NewJob(source + "x", "longer", Now.AddHours(-1)));

        var body = await ListAsync($"source={source}");

        Assert.Equal(["exact"], body.Items.Select(j => j.Title));
    }

    [Fact]
    public async Task List_excludes_jobs_posted_more_than_24_hours_ago()
    {
        var source = UniqueSource();
        await SeedAsync(
            NewJob(source, "recent", Now.AddHours(-23)),
            NewJob(source, "too-old", Now.AddHours(-25)));

        var body = await ListAsync($"source={source}");

        Assert.Equal(["recent"], body.Items.Select(j => j.Title));
    }

    [Fact]
    public async Task List_postedWithinHours_narrows_the_window()
    {
        var source = UniqueSource();
        await SeedAsync(
            NewJob(source, "one-hour", Now.AddHours(-1)),
            NewJob(source, "five-hours", Now.AddHours(-5)));

        var body = await ListAsync($"source={source}&postedWithinHours=2");

        Assert.Equal(["one-hour"], body.Items.Select(j => j.Title));
    }

    [Fact]
    public async Task List_returns_plain_text_excerpt_of_at_most_300_characters()
    {
        var source = UniqueSource();
        var longText = new string('a', 400);
        await SeedAsync(
            NewJob(source, "html", Now.AddHours(-1), description: "<p>Build <b>APIs</b> in C# &amp; .NET</p>"),
            NewJob(source, "long", Now.AddHours(-2), description: $"<div>{longText}</div>"),
            NewJob(source, "empty", Now.AddHours(-3), description: null));

        var body = await ListAsync($"source={source}");

        var items = body.Items.ToDictionary(j => j.Title);
        Assert.Equal("Build APIs in C# & .NET", items["html"].DescriptionExcerpt);
        Assert.Equal(new string('a', 300), items["long"].DescriptionExcerpt);
        Assert.Null(items["empty"].DescriptionExcerpt);
    }

    [Theory]
    [InlineData("page=0", "page must be 1 or greater.")]
    [InlineData("page=-1", "page must be 1 or greater.")]
    [InlineData("pageSize=0", "pageSize must be between 1 and 100.")]
    [InlineData("pageSize=101", "pageSize must be between 1 and 100.")]
    [InlineData("postedWithinHours=0", "postedWithinHours must be between 1 and 24.")]
    [InlineData("postedWithinHours=25", "postedWithinHours must be between 1 and 24.")]
    public async Task List_invalid_paging_returns_400_with_message(string queryString, string expectedMessage)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/jobs?{queryString}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains(expectedMessage, problem.Errors.SelectMany(e => e.Value));
    }

    [Fact]
    public async Task List_non_numeric_page_returns_400()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/jobs?page=abc");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_existing_job_returns_full_job_with_description()
    {
        var source = UniqueSource();
        var job = NewJob(source, "Senior .NET Developer", Now.AddHours(-1),
            company: "Acme", location: "Remote, Europe", description: "<p>Full description</p>");
        job.EmploymentTypes = "Full-Time";
        job.Level = "Senior";
        await SeedAsync(job);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/jobs/{job.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JobDetailResponse>();
        Assert.NotNull(body);
        Assert.Equal(job.Id, body.Id);
        Assert.Equal(source, body.Source);
        Assert.Equal(job.ExternalId, body.ExternalId);
        Assert.Equal(job.Url, body.Url);
        Assert.Equal("Senior .NET Developer", body.Title);
        Assert.Equal("Acme", body.Company);
        Assert.Equal("Remote, Europe", body.Location);
        Assert.Equal("Full-Time", body.EmploymentTypes);
        Assert.Equal("Senior", body.Level);
        Assert.Equal("<p>Full description</p>", body.Description);
        Assert.Equal(job.PostedAt, body.PostedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(job.FetchedAt, body.FetchedAt, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Get_unknown_id_returns_404()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/jobs/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_describes_both_jobs_endpoints_and_their_responses()
    {
        using var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));

        var paths = document.RootElement.GetProperty("paths");
        var list = paths.GetProperty("/api/jobs").GetProperty("get");
        var detail = paths.GetProperty("/api/jobs/{id}").GetProperty("get");
        Assert.Equal(["200", "400"], ResponseCodes(list));
        Assert.Equal(["200", "404"], ResponseCodes(detail));
        var parameters = list.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList();
        foreach (var name in new[] { "Page", "PageSize", "Q", "Location", "Source", "PostedWithinHours" })
            Assert.Contains(parameters, p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));

        static List<string> ResponseCodes(JsonElement operation) =>
            operation.GetProperty("responses").EnumerateObject().Select(r => r.Name).Order().ToList();
    }

    private static string UniqueSource() => $"api-test-{Guid.NewGuid():N}";

    private static Job NewJob(
        string source,
        string title,
        DateTimeOffset postedAt,
        string company = "Example Co",
        string? location = "Europe",
        string? description = null,
        Guid? id = null)
    {
        var externalId = Guid.NewGuid().ToString("N");
        return new Job
        {
            Id = id ?? Guid.NewGuid(),
            Source = source,
            ExternalId = externalId,
            Url = $"https://example.test/jobs/{externalId}",
            Title = title,
            Company = company,
            Location = location,
            Description = description,
            PostedAt = postedAt,
            FetchedAt = Now,
        };
    }

    private async Task SeedAsync(params Job[] jobs)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SlofferDbContext>();
        db.Jobs.AddRange(jobs);
        await db.SaveChangesAsync();
    }

    private async Task<JobListResponse> ListAsync(string queryString)
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/jobs?{queryString}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JobListResponse>();
        Assert.NotNull(body);
        return body;
    }
}
