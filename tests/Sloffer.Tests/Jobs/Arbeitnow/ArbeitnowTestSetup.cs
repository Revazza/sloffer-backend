using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sloffer.Jobs.Fetching;
using Sloffer.Jobs.Sources.Arbeitnow;
using Sloffer.Tests.Jobs.Jobicy;

namespace Sloffer.Tests.Jobs.Arbeitnow;

/// <summary>
/// Completes every <c>Task.Delay(..., timeProvider)</c> at once and records the requested delays (and when they
/// happened, relative to HTTP requests, in a shared <see cref="Events"/> log).
/// </summary>
internal sealed class RecordingDelayTimeProvider(List<string> events) : TimeProvider
{
    public List<string> Events { get; } = events;

    public List<TimeSpan> Delays { get; } = [];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (Events)
        {
            Delays.Add(dueTime);
            Events.Add($"delay {dueTime}");
        }

        ThreadPool.QueueUserWorkItem(_ => callback(state));
        return new NoopTimer();
    }

    private sealed class NoopTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>Collects log messages (formatted) so tests can check the per-run log line.</summary>
internal sealed class ListLoggerProvider : ILoggerProvider
{
    public List<string> Messages { get; } = [];

    public ILogger CreateLogger(string categoryName) => new ListLogger(Messages);

    public void Dispose() { }

    private sealed class ListLogger(List<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (messages)
                messages.Add(formatter(state, exception));
        }
    }
}

/// <summary>One listing for a generated page.</summary>
internal sealed record ArbeitnowListing(
    string Slug,
    DateTimeOffset CreatedAt,
    string Title = "Senior C# Developer",
    string? Description = "<p>Backend work.</p>",
    string[]? Tags = null,
    string? Location = "Berlin",
    bool Remote = false);

internal static class ArbeitnowTestSetup
{
    /// <summary>A trimmed, anonymised response with the shape of a real <c>GET api/job-board-api</c> response.</summary>
    public static string SampleResponse() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Jobs", "Arbeitnow", "Fixtures", "arbeitnow-job-board.json"));

    /// <summary>A page in the real response shape. <paramref name="hasNext"/> sets <c>links.next</c>.</summary>
    public static string Page(int page, bool hasNext, params ArbeitnowListing[] listings) =>
        JsonSerializer.Serialize(new
        {
            data = listings.Select(l => new
            {
                slug = l.Slug,
                company_name = "Example GmbH",
                title = l.Title,
                description = l.Description,
                remote = l.Remote,
                url = $"https://www.arbeitnow.com/jobs/companies/example/{l.Slug}",
                tags = l.Tags ?? ["Engineering"],
                job_types = new[] { "Full Time" },
                location = l.Location,
                created_at = l.CreatedAt.ToUnixTimeSeconds(),
            }),
            links = new
            {
                first = "https://www.arbeitnow.com/api/job-board-api?page=1",
                last = (string?)null,
                prev = page > 1 ? $"https://www.arbeitnow.com/api/job-board-api?page={page - 1}" : null,
                next = hasNext ? $"https://www.arbeitnow.com/api/job-board-api?page={page + 1}" : null,
            },
            meta = new { current_page = page, per_page = 300 },
        });

    /// <summary>Answers <c>?page=N</c> with <paramref name="pages"/>[N-1] and logs each request to <paramref name="events"/>.</summary>
    public static StubHttpMessageHandler Pages(List<string> events, params string[] pages) =>
        new(request =>
        {
            var query = request.RequestUri!.Query;
            var page = int.Parse(query[(query.IndexOf("page=", StringComparison.Ordinal) + 5)..]);
            lock (events)
                events.Add($"GET page={page}");
            var json = page <= pages.Length ? pages[page - 1] : Page(page, hasNext: false);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            };
        });

    /// <summary>The real Arbeitnow registration, with the primary HTTP handler swapped and delays recorded, not waited.</summary>
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
        services.AddArbeitnowSource(configuration).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider(validateScopes: true);
    }

    public static async Task<IReadOnlyList<FetchedJob>> FetchAsync(ServiceProvider services, DateTimeOffset since)
    {
        await using var scope = services.CreateAsyncScope();
        var source = Assert.Single(scope.ServiceProvider.GetServices<IJobSource>());
        return await source.FetchRecentAsync(since, CancellationToken.None);
    }
}
