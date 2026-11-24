using Sloffer.Data;

namespace Sloffer.Api.Jobs;

/// <summary>
/// The job age window as seen by the API, bound from the same <c>JobFetch:MaxAgeDays</c> key the Jobs worker uses.
/// <c>GET /api/jobs</c> defaults <c>postedWithinHours</c> to <c>MaxAgeDays * 24</c>.
/// </summary>
public sealed class JobListOptions
{
    public int MaxAgeDays { get; set; } = JobAgeWindow.DefaultMaxAgeDays;

    public int DefaultPostedWithinHours => MaxAgeDays * 24;
}
