using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sloffer.Jobs.Fetching;
using Sloffer.Jobs.Sources.Jobicy;

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

    /// <summary>The real Jobicy registration, with the primary HTTP handler swapped for <paramref name="handler"/>.</summary>
    public static ServiceProvider CreateServices(HttpMessageHandler handler, IDictionary<string, string?>? config = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config ?? new Dictionary<string, string?>()).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddJobFetching(configuration);
        services.AddJobicySource(configuration).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider(validateScopes: true);
    }

    public static async Task<IReadOnlyList<FetchedJob>> FetchAsync(ServiceProvider services, DateTimeOffset since)
    {
        await using var scope = services.CreateAsyncScope();
        var source = Assert.Single(scope.ServiceProvider.GetServices<IJobSource>());
        return await source.FetchRecentAsync(since, CancellationToken.None);
    }
}
