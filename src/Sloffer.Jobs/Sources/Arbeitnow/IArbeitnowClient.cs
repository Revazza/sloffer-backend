using RestEase;

namespace Sloffer.Jobs.Sources.Arbeitnow;

/// <summary>
/// Arbeitnow's public job board API (no auth): https://www.arbeitnow.com/blog/job-board-api.
/// "Free public API, please do not abuse." Non-success responses throw <see cref="ApiException"/>.
/// </summary>
public interface IArbeitnowClient
{
    /// <summary>One page of listings (300 per page), ordered by <c>created_at</c>, roughly newest first.</summary>
    /// <param name="page">1-based page number.</param>
    [Get("api/job-board-api")]
    Task<ArbeitnowResponse> GetJobsAsync([Query("page")] int page, CancellationToken ct);
}
