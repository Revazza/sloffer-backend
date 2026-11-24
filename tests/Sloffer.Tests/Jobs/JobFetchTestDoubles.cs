using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sloffer.Data;
using Sloffer.Jobs.Fetching;

namespace Sloffer.Tests.Jobs;

/// <summary>A clock the test moves by hand.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; private set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>An <see cref="IJobSource"/> that returns canned jobs (or throws) and records its calls.</summary>
internal sealed class FakeJobSource(string name, Func<DateTimeOffset, IReadOnlyList<FetchedJob>> fetch) : IJobSource
{
    public string Name { get; } = name;

    public List<DateTimeOffset> Calls { get; } = [];

    public Task<IReadOnlyList<FetchedJob>> FetchRecentAsync(DateTimeOffset since, CancellationToken ct)
    {
        Calls.Add(since);
        return Task.FromResult(fetch(since));
    }

    public static FakeJobSource Returning(string name, params FetchedJob[] jobs) => new(name, _ => jobs);

    public static FakeJobSource Throwing(string name, Exception exception) => new(name, _ => throw exception);
}

internal static class JobFetchTestSetup
{
    public static readonly DateTimeOffset Start = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Unique per test, so tests sharing the test database never see each other's rows.</summary>
    public static string UniqueSourceName(string prefix = "fake") => $"{prefix}-{Guid.NewGuid():N}";

    public static FetchedJob Job(string source, string externalId, DateTimeOffset postedAt) =>
        new(source, externalId, $"https://example.test/jobs/{externalId}", $"Title {externalId}", "Example Co",
            Location: null, EmploymentTypes: null, Level: null, Description: null, postedAt);

    public static ServiceProvider CreateServices(
        TimeProvider time,
        IEnumerable<IJobSource> sources,
        IDictionary<string, string?>? config = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config ?? new Dictionary<string, string?>()).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<SlofferDbContext>(o => o.UseNpgsql(TestDatabase.ConnectionString));
        services.AddSingleton(time);
        services.AddJobFetching(configuration);
        foreach (var source in sources)
            services.AddSingleton(source);

        return services.BuildServiceProvider(validateScopes: true);
    }

    public static async Task<List<Job>> StoredJobsAsync(IServiceProvider services, string source)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SlofferDbContext>();
        return await db.Jobs.AsNoTracking().Where(j => j.Source == source).OrderBy(j => j.ExternalId).ToListAsync();
    }
}
