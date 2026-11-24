namespace Sloffer.Jobs.Fetching;

/// <summary>Bound from the <c>JobFetch</c> config section. Defaults live here; appsettings may override them.</summary>
public sealed class JobFetchOptions
{
    public const string SectionName = "JobFetch";

    /// <summary>Minimum time between two fetches of the same source, unless overridden in <see cref="Sources"/>.</summary>
    public TimeSpan MinInterval { get; set; } = TimeSpan.FromMinutes(60);

    /// <summary>Delay between two worker cycles (each cycle checks every source).</summary>
    public TimeSpan CycleDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Per-source overrides, keyed by <see cref="IJobSource.Name"/>.</summary>
    public Dictionary<string, JobSourceOptions> Sources { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public TimeSpan GetMinInterval(string sourceName) =>
        Sources.TryGetValue(sourceName, out var source) && source.MinInterval is { } interval ? interval : MinInterval;
}

public sealed class JobSourceOptions
{
    public TimeSpan? MinInterval { get; set; }
}
