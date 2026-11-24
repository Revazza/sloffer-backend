namespace Sloffer.Jobs.Fetching;

/// <summary>Settings for <see cref="JobFetchService"/>, bound from the <c>JobFetch</c> config section.</summary>
public sealed class JobFetchOptions
{
    public const string SectionName = "JobFetch";

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
