using RestEase;

namespace Sloffer.Jobs.Sources.Remotive;

/// <summary>
/// Remotive's public remote-jobs API (no auth): https://remotive.com/api/remote-jobs.
/// Terms: link back to the job URL on Remotive and name Remotive as the source; call it only a few times a day
/// (listings are published with a delay anyway). Non-success responses throw <see cref="ApiException"/>.
/// </summary>
public interface IRemotiveClient
{
    /// <summary>All current listings of one category (e.g. <c>software-dev</c>), newest first.</summary>
    [Get("api/remote-jobs")]
    Task<RemotiveResponse> GetJobsAsync([Query("category")] string category, CancellationToken ct);
}
