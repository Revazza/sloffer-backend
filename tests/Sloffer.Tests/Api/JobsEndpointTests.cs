using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
    public async Task List_by_default_returns_jobs_from_the_last_7_days()
    {
        var source = UniqueSource();
        await SeedAsync(
            NewJob(source, "yesterday", Now.AddHours(-25)),
            NewJob(source, "six-days", Now.AddDays(-6)),
            NewJob(source, "too-old", Now.AddDays(-7).AddHours(-1)));

        var body = await ListAsync($"source={source}");

        Assert.Equal(["yesterday", "six-days"], body.Items.Select(j => j.Title));
    }

    [Fact]
    public async Task List_postedWithinHours_24_returns_only_new_today()
    {
        var source = UniqueSource();
        await SeedAsync(
            NewJob(source, "recent", Now.AddHours(-23)),
            NewJob(source, "yesterday", Now.AddHours(-25)));

        var body = await ListAsync($"source={source}&postedWithinHours=24");

        Assert.Equal(["recent"], body.Items.Select(j => j.Title));
    }

    [Fact]
    public async Task List_postedWithinHours_720_returns_jobs_from_the_last_30_days()
    {
        var source = UniqueSource();
        await SeedAsync(
            NewJob(source, "twenty-days", Now.AddDays(-20)),
            NewJob(source, "too-old", Now.AddDays(-31)));

        var body = await ListAsync($"source={source}&postedWithinHours=720");

        Assert.Equal(["twenty-days"], body.Items.Select(j => j.Title));
    }

    [Fact]
    public async Task List_default_window_follows_JobFetch_MaxAgeDays()
    {
        var source = UniqueSource();
        await SeedAsync(
            NewJob(source, "one-day", Now.AddDays(-1)),
            NewJob(source, "three-days", Now.AddDays(-3)));
        await using var customFactory = factory.WithWebHostBuilder(b => b.UseSetting("JobFetch:MaxAgeDays", "2"));
        using var client = customFactory.CreateClient();

        var body = await client.GetFromJsonAsync<JobListResponse>($"/api/jobs?source={source}");

        Assert.NotNull(body);
        Assert.Equal(["one-day"], body.Items.Select(j => j.Title));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("31")]
    public void Api_with_MaxAgeDays_outside_1_to_30_fails_at_startup(string value)
    {
        using var invalidFactory = factory.WithWebHostBuilder(b => b.UseSetting("JobFetch:MaxAgeDays", value));

        var error = Assert.Throws<OptionsValidationException>(() => invalidFactory.CreateClient());
        Assert.Contains("JobFetch:MaxAgeDays must be between 1 and 30 (days).", error.Failures);
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
    [InlineData("postedWithinHours=0", "postedWithinHours must be between 1 and 720.")]
    [InlineData("postedWithinHours=721", "postedWithinHours must be between 1 and 720.")]
    [InlineData("minScore=-1", "minScore must be between 0 and 100.")]
    [InlineData("minScore=101", "minScore must be between 0 and 100.")]
    [InlineData("sort=newest", "sort must be 'score' or 'posted'.")]
    [InlineData("sort=SCORE", "sort must be 'score' or 'posted'.")]
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
        foreach (var name in new[] { "Page", "PageSize", "Q", "Location", "Source", "PostedWithinHours", "IncludeDuplicates", "MinScore", "Sort" })
            Assert.Contains(parameters, p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
        var itemSchema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("JobListItem");
        Assert.True(itemSchema.GetProperty("properties").TryGetProperty("duplicateOfId", out _));
        Assert.True(itemSchema.GetProperty("properties").TryGetProperty("score", out _));
        Assert.True(itemSchema.GetProperty("properties").TryGetProperty("scoreReasons", out _));
        var detailSchema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("JobDetailResponse");
        Assert.True(detailSchema.GetProperty("properties").TryGetProperty("score", out _));
        Assert.True(detailSchema.GetProperty("properties").TryGetProperty("scoreReasons", out _));
        var postedWithinHours = list.GetProperty("parameters").EnumerateArray()
            .Single(p => string.Equals(p.GetProperty("name").GetString(), "PostedWithinHours", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("JobFetch:MaxAgeDays * 24", postedWithinHours.GetProperty("description").GetString());
        Assert.DoesNotContain("1 to 24.", postedWithinHours.GetRawText());

        static List<string> ResponseCodes(JsonElement operation) =>
            operation.GetProperty("responses").EnumerateObject().Select(r => r.Name).Order().ToList();
    }

    [Fact]
    public async Task List_hides_duplicates_by_default_and_counts_only_originals()
    {
        var source = UniqueSource();
        var original = NewJob(source, "original", Now.AddHours(-2));
        var duplicate = NewJob(source, "duplicate", Now.AddHours(-1));
        duplicate.DuplicateOfId = original.Id;
        await SeedAsync(original);
        await SeedAsync(duplicate);

        var body = await ListAsync($"source={source}");

        var item = Assert.Single(body.Items);
        Assert.Equal("original", item.Title);
        Assert.Null(item.DuplicateOfId);
        Assert.Equal(1, body.TotalCount);
    }

    [Fact]
    public async Task List_includeDuplicates_true_shows_duplicates_with_duplicateOfId()
    {
        var source = UniqueSource();
        var original = NewJob(source, "original", Now.AddHours(-2));
        var duplicate = NewJob(source, "duplicate", Now.AddHours(-1));
        duplicate.DuplicateOfId = original.Id;
        await SeedAsync(original);
        await SeedAsync(duplicate);

        var body = await ListAsync($"source={source}&includeDuplicates=true");

        Assert.Equal(2, body.TotalCount);
        var items = body.Items.ToDictionary(j => j.Title);
        Assert.Null(items["original"].DuplicateOfId);
        Assert.Equal(original.Id, items["duplicate"].DuplicateOfId);
    }

    [Fact]
    public async Task List_by_default_shows_only_good_matches_55_plus_and_unscored_jobs()
    {
        var source = UniqueSource();
        await SeedAsync(
            NewJob(source, "excellent", Now.AddHours(-1), score: 80),
            NewJob(source, "good-edge", Now.AddHours(-2), score: 55),
            NewJob(source, "maybe", Now.AddHours(-3), score: 54),
            NewJob(source, "rejected", Now.AddHours(-4), score: 0),
            NewJob(source, "unscored", Now.AddHours(-5)));

        var body = await ListAsync($"source={source}");

        Assert.Equal(["excellent", "good-edge", "unscored"], body.Items.Select(j => j.Title));
        Assert.Equal(3, body.TotalCount);
    }

    [Fact]
    public async Task List_minScore_1_hides_only_rejected_jobs()
    {
        var source = UniqueSource();
        await SeedAsync(
            NewJob(source, "maybe", Now.AddHours(-1), score: 40),
            NewJob(source, "rejected", Now.AddHours(-2), score: 0));

        var body = await ListAsync($"source={source}&minScore=1");

        Assert.Equal(["maybe"], body.Items.Select(j => j.Title));
    }

    [Fact]
    public async Task List_minScore_0_shows_all_jobs()
    {
        var source = UniqueSource();
        await SeedAsync(
            NewJob(source, "good", Now.AddHours(-1), score: 70),
            NewJob(source, "rejected", Now.AddHours(-2), score: 0),
            NewJob(source, "unscored", Now.AddHours(-3)));

        var body = await ListAsync($"source={source}&minScore=0");

        Assert.Equal(["good", "rejected", "unscored"], body.Items.Select(j => j.Title));
        Assert.Equal(3, body.TotalCount);
    }

    [Fact]
    public async Task List_minScore_hides_scored_jobs_below_it()
    {
        var source = UniqueSource();
        await SeedAsync(
            NewJob(source, "fifty", Now.AddHours(-1), score: 50),
            NewJob(source, "forty-nine", Now.AddHours(-2), score: 49),
            NewJob(source, "unscored", Now.AddHours(-3)));

        var body = await ListAsync($"source={source}&minScore=50");

        Assert.Equal(["fifty", "unscored"], body.Items.Select(j => j.Title));
        Assert.Equal(2, body.TotalCount);
    }

    [Fact]
    public async Task List_by_default_sorts_by_score_descending_with_unscored_last_then_newest()
    {
        var source = UniqueSource();
        await SeedAsync(
            NewJob(source, "unscored-new", Now.AddMinutes(-1)),
            NewJob(source, "low", Now.AddMinutes(-2), score: 60),
            NewJob(source, "high-old", Now.AddHours(-5), score: 80),
            NewJob(source, "high-new", Now.AddHours(-1), score: 80),
            NewJob(source, "unscored-old", Now.AddHours(-6)));

        var byDefault = await ListAsync($"source={source}");
        var explicitScore = await ListAsync($"source={source}&sort=score");

        string[] expected = ["high-new", "high-old", "low", "unscored-new", "unscored-old"];
        Assert.Equal(expected, byDefault.Items.Select(j => j.Title));
        Assert.Equal(expected, explicitScore.Items.Select(j => j.Title));
    }

    [Fact]
    public async Task List_sort_posted_returns_newest_first_regardless_of_score()
    {
        var source = UniqueSource();
        await SeedAsync(
            NewJob(source, "old-high", Now.AddHours(-5), score: 90),
            NewJob(source, "new-low", Now.AddHours(-1), score: 60),
            NewJob(source, "mid-unscored", Now.AddHours(-3)));

        var body = await ListAsync($"source={source}&sort=posted");

        Assert.Equal(["new-low", "mid-unscored", "old-high"], body.Items.Select(j => j.Title));
    }

    [Fact]
    public async Task List_returns_score_and_reasons_and_empty_reasons_when_unscored()
    {
        var source = UniqueSource();
        await SeedAsync(
            NewJob(source, "scored", Now.AddHours(-1), score: 65, reasons: [".NET role", "EMEA remote"]),
            NewJob(source, "unscored", Now.AddHours(-2)));

        var body = await ListAsync($"source={source}");

        var items = body.Items.ToDictionary(j => j.Title);
        Assert.Equal(65, items["scored"].Score);
        Assert.Equal([".NET role", "EMEA remote"], items["scored"].ScoreReasons);
        Assert.Null(items["unscored"].Score);
        Assert.Empty(items["unscored"].ScoreReasons);
    }

    [Fact]
    public async Task List_unscored_reasons_are_an_empty_json_array_not_null()
    {
        var source = UniqueSource();
        await SeedAsync(NewJob(source, "unscored", Now.AddHours(-1)));
        using var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync($"/api/jobs?source={source}"));

        var item = document.RootElement.GetProperty("items")[0];
        Assert.Equal(JsonValueKind.Null, item.GetProperty("score").ValueKind);
        Assert.Equal(JsonValueKind.Array, item.GetProperty("scoreReasons").ValueKind);
        Assert.Equal(0, item.GetProperty("scoreReasons").GetArrayLength());
    }

    [Fact]
    public async Task Get_returns_score_and_reasons()
    {
        var source = UniqueSource();
        var scored = NewJob(source, "scored", Now.AddHours(-1), score: 0, reasons: ["Not a .NET role"]);
        var unscored = NewJob(source, "unscored", Now.AddHours(-1));
        await SeedAsync(scored, unscored);
        using var client = factory.CreateClient();

        var scoredBody = await client.GetFromJsonAsync<JobDetailResponse>($"/api/jobs/{scored.Id}");
        var unscoredBody = await client.GetFromJsonAsync<JobDetailResponse>($"/api/jobs/{unscored.Id}");

        Assert.NotNull(scoredBody);
        Assert.Equal(0, scoredBody.Score);
        Assert.Equal(["Not a .NET role"], scoredBody.ScoreReasons);
        Assert.NotNull(unscoredBody);
        Assert.Null(unscoredBody.Score);
        Assert.Empty(unscoredBody.ScoreReasons);
    }

    [Fact]
    public async Task MarkViewed_sets_viewedAt_once_and_list_and_detail_return_it()
    {
        var source = UniqueSource();
        var job = NewJob(source, "seen", Now.AddHours(-1), score: 80);
        await SeedAsync(job, NewJob(source, "unseen", Now.AddHours(-2), score: 80));
        using var client = factory.CreateClient();

        var first = await client.PostAsync($"/api/jobs/{job.Id}/viewed", null);
        var firstViewedAt = (await client.GetFromJsonAsync<JobDetailResponse>($"/api/jobs/{job.Id}"))?.ViewedAt;
        var second = await client.PostAsync($"/api/jobs/{job.Id}/viewed", null);
        var detail = await client.GetFromJsonAsync<JobDetailResponse>($"/api/jobs/{job.Id}");
        var list = await ListAsync($"source={source}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.NotNull(firstViewedAt);
        Assert.Equal(firstViewedAt, detail?.ViewedAt);
        var items = list.Items.ToDictionary(j => j.Title);
        Assert.Equal(firstViewedAt, items["seen"].ViewedAt);
        Assert.Null(items["unseen"].ViewedAt);
    }

    [Fact]
    public async Task List_hideViewed_hides_viewed_jobs_and_counts_only_the_rest()
    {
        var source = UniqueSource();
        var seen = NewJob(source, "seen", Now.AddHours(-1), score: 80);
        await SeedAsync(seen, NewJob(source, "unseen", Now.AddHours(-2), score: 80));
        using var client = factory.CreateClient();
        await client.PostAsync($"/api/jobs/{seen.Id}/viewed", null);

        var hidden = await ListAsync($"source={source}&hideViewed=true");
        var shown = await ListAsync($"source={source}");

        Assert.Equal(["unseen"], hidden.Items.Select(j => j.Title));
        Assert.Equal(1, hidden.TotalCount);
        Assert.Equal(2, shown.TotalCount);
    }

    [Fact]
    public async Task UnmarkViewed_clears_viewedAt()
    {
        var source = UniqueSource();
        var job = NewJob(source, "seen", Now.AddHours(-1), score: 80);
        await SeedAsync(job);
        using var client = factory.CreateClient();
        await client.PostAsync($"/api/jobs/{job.Id}/viewed", null);

        var response = await client.DeleteAsync($"/api/jobs/{job.Id}/viewed");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null((await client.GetFromJsonAsync<JobDetailResponse>($"/api/jobs/{job.Id}"))?.ViewedAt);
    }

    [Fact]
    public async Task Viewed_endpoints_return_404_for_unknown_jobs()
    {
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/jobs/{id}/viewed", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/jobs/{id}/viewed")).StatusCode);
    }

    [Fact]
    public async Task OpenApi_describes_viewed_endpoints_and_fields()
    {
        using var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));

        var viewed = document.RootElement.GetProperty("paths").GetProperty("/api/jobs/{id}/viewed");
        Assert.True(viewed.TryGetProperty("post", out _));
        Assert.True(viewed.TryGetProperty("delete", out _));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.GetProperty("JobListItem").GetProperty("properties").TryGetProperty("viewedAt", out _));
        Assert.True(schemas.GetProperty("JobDetailResponse").GetProperty("properties").TryGetProperty("viewedAt", out _));
        var parameters = document.RootElement.GetProperty("paths").GetProperty("/api/jobs").GetProperty("get")
            .GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString());
        Assert.Contains(parameters, p => string.Equals(p, "HideViewed", StringComparison.OrdinalIgnoreCase));
    }

    private static string UniqueSource() => $"api-test-{Guid.NewGuid():N}";

    private static Job NewJob(
        string source,
        string title,
        DateTimeOffset postedAt,
        string company = "Example Co",
        string? location = "Europe",
        string? description = null,
        Guid? id = null,
        int? score = null,
        List<string>? reasons = null)
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
            Score = score,
            ScoreReasons = score is null ? null : reasons ?? [],
            ScoredAt = score is null ? null : Now,
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
