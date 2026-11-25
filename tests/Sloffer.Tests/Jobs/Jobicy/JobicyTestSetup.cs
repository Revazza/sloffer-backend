using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sloffer.Jobs.Fetching;
using Sloffer.Jobs.Sources.Jobicy;
using Sloffer.Tests.Jobs.Arbeitnow;

namespace Sloffer.Tests.Jobs.Jobicy;

/// <summary>Answers every request with a canned response and records the requests. No network.</summary>
internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }

    public static StubHttpMessageHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    public static StubHttpMessageHandler Status(HttpStatusCode status) =>
        new(_ => new HttpResponseMessage(status) { Content = new StringContent("") });
}

internal static class JobicyTestSetup
{
    /// <summary>Trimmed capture of a real <c>GET api/v2/remote-jobs?geo=europe&amp;industry=engineering</c> response (plus one old listing).</summary>
    public static string SampleResponse() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Jobs", "Jobicy", "Fixtures", "jobicy-remote-jobs.json"));

    /// <summary>
    /// The real Jobicy registration, with the primary HTTP handler swapped for <paramref name="handler"/> and delays
    /// between queries recorded, not waited.
    /// </summary>
    public static ServiceProvider CreateServices(
        HttpMessageHandler handler,
        IDictionary<string, string?>? config = null,
        TimeProvider? time = null,
        ILoggerProvider? logs = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config ?? new Dictionary<string, string?>()).Build();

        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            if (logs is not null)
                b.AddProvider(logs);
        });
        services.AddSingleton(time ?? new RecordingDelayTimeProvider([]));
        services.AddJobFetching(configuration);
        services.AddJobicySource(configuration).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>A response in the real shape with one minimal listing per id, all posted at <paramref name="postedAt"/>.</summary>
    public static string Listings(DateTimeOffset postedAt, params int[] ids) =>
        JsonSerializer.Serialize(new
        {
            jobs = ids.Select(id => new
            {
                id,
                url = $"https://jobicy.com/jobs/{id}-dotnet-developer",
                jobTitle = $".NET Developer {id}",
                companyName = "Example Co",
                jobGeo = "Europe",
                pubDate = postedAt.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
            }),
        });

    /// <summary>Answers each request with <paramref name="respond"/>(query string without <c>count</c>), logging it to <paramref name="events"/>.</summary>
    public static StubHttpMessageHandler ByQuery(Func<string, HttpResponseMessage> respond, List<string>? events = null) =>
        new(request =>
        {
            var query = string.Join("&", request.RequestUri!.Query.TrimStart('?').Split('&').Where(p => !p.StartsWith("count=", StringComparison.Ordinal)));
            if (events is not null)
                lock (events)
                    events.Add($"GET {query}");
            return respond(query);
        });

    public static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static async Task<IReadOnlyList<FetchedJob>> FetchAsync(ServiceProvider services, DateTimeOffset since)
    {
        await using var scope = services.CreateAsyncScope();
        var source = Assert.Single(scope.ServiceProvider.GetServices<IJobSource>());
        return await source.FetchRecentAsync(since, CancellationToken.None);
    }
}
