using Sloffer.Data;

namespace Sloffer.Jobs.Fetching;

/// <summary>Settings for <see cref="JobFetchService"/>, bound from the <c>JobFetch</c> config section.</summary>
public sealed class JobFetchOptions
{
    public const string SectionName = JobAgeWindow.SectionName;

    /// <summary>
    /// Age window in days (1-30, default 7): sources are asked for listings posted since now minus this window,
    /// and older listings are dropped before storing. Already stored jobs are never removed when they age out.
    /// </summary>
    public int MaxAgeDays { get; set; } = JobAgeWindow.DefaultMaxAgeDays;

    public TimeSpan MaxAge => TimeSpan.FromDays(MaxAgeDays);

    /// <summary>
    /// On worker startup, compute <c>DedupKey</c> and <c>DuplicateOfId</c> for stored rows that have no key yet
    /// (see <see cref="Dedup.JobDedupBackfillService"/>). Default false.
    /// </summary>
    public bool BackfillDedupOnStartup { get; set; }

    /// <summary>Pause between fetch cycles. Each cycle only calls sources whose <c>MinInterval</c> has elapsed.</summary>
    public TimeSpan CycleDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Minimum time between two fetches of the same source, unless overridden in <see cref="Sources"/>.</summary>
    public TimeSpan MinInterval { get; set; } = TimeSpan.FromMinutes(60);

    /// <summary>Per-source overrides, keyed by <see cref="IJobSource.Name"/> (case-insensitive).</summary>
    public Dictionary<string, JobFetchSourceOptions> Sources { get; set; } = [];

    public TimeSpan GetMinInterval(string sourceName)
    {
        foreach (var (name, source) in Sources)
        {
            if (string.Equals(name, sourceName, StringComparison.OrdinalIgnoreCase) && source.MinInterval is { } interval)
                return interval;
        }

        return MinInterval;
    }

    internal bool IsValid() =>
        CycleDelay > TimeSpan.Zero
        && MinInterval >= TimeSpan.Zero
        && Sources.Values.All(s => s.MinInterval is null || s.MinInterval >= TimeSpan.Zero);
}

public sealed class JobFetchSourceOptions
{
    /// <summary>Minimum time between two fetches of this source. Null uses <see cref="JobFetchOptions.MinInterval"/>.</summary>
    public TimeSpan? MinInterval { get; set; }
}
