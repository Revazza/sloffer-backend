using RestEase;

namespace Sloffer.Jobs.Sources.Jobicy;

/// <summary>
/// Jobicy's public remote-jobs API (no auth): https://github.com/Jobicy/remote-jobs-api.
/// Do not poll more than once per hour. Non-success responses throw <see cref="ApiException"/>.
/// </summary>
public interface IJobicyApi
{
    /// <summary>Latest listings, newest first. Null query values are omitted.</summary>
    /// <param name="count">Number of listings, 1-200.</param>
    /// <param name="geo">Region slug, e.g. <c>europe</c>, <c>usa</c>, <c>anywhere</c>.</param>
    /// <param name="industry">Industry slug, e.g. <c>engineering</c>.</param>
    [Get("api/v2/remote-jobs")]
    Task<JobicyResponse> GetRemoteJobsAsync(
        [Query("count")] int? count,
        [Query("geo")] string? geo,
        [Query("industry")] string? industry,
        CancellationToken ct);
}
