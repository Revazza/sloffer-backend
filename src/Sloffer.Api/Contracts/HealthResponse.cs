namespace Sloffer.Api.Contracts;

/// <param name="Status">Healthy, Degraded or Unhealthy.</param>
/// <param name="Checks">Each dependency's status, keyed by name (e.g. "database").</param>
public sealed record HealthResponse(string Status, IReadOnlyDictionary<string, string> Checks);
