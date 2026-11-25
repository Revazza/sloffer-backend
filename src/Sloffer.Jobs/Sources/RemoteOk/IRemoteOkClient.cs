using RestEase;

namespace Sloffer.Jobs.Sources.RemoteOk;

/// <summary>
/// Remote OK's public API (no auth): https://remoteok.com/api. Returns the latest ~100 remote jobs; the first array
/// element is a legal notice. Terms: link back to the job URL on Remote OK (followed link) and name "Remote OK" as the
/// source; never use the Remote OK logo. Non-success responses throw <see cref="ApiException"/>.
/// </summary>
public interface IRemoteOkClient
{
    [Get("api")]
    Task<List<RemoteOkJob>> GetJobsAsync(CancellationToken ct);
}
