namespace Sloffer.Data;

/// <summary>
/// The job age window shared by the Jobs worker and the API: config key <c>JobFetch:MaxAgeDays</c>.
/// The worker only requests and stores listings posted within the window; the API lists them by default.
/// Stored jobs are never deleted or changed when they fall out of the window.
/// </summary>
public static class JobAgeWindow
{
    public const string SectionName = "JobFetch";
    public const int DefaultMaxAgeDays = 7;
    public const int MinMaxAgeDays = 1;
    public const int MaxMaxAgeDays = 30;

    public const string ValidationMessage = "JobFetch:MaxAgeDays must be between 1 and 30 (days).";

    public static bool IsValid(int maxAgeDays) => maxAgeDays is >= MinMaxAgeDays and <= MaxMaxAgeDays;
}
