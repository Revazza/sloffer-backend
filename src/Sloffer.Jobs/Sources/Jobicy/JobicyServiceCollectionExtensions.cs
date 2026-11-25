using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using RestEase.HttpClientFactory;
using Sloffer.Jobs.Fetching;

namespace Sloffer.Jobs.Sources.Jobicy;

public static class JobicyServiceCollectionExtensions
{
    public static readonly Uri BaseAddress = new("https://jobicy.com/");

    public const string UserAgent = "Sloffer/1.0 (personal job aggregator; +https://github.com/revazza/sloffer-backend)";

    /// <summary>
    /// Registers <see cref="JobicyOptions"/> (<c>JobFetch:Jobicy</c>), the <see cref="IJobicyApi"/> RestEase client
    /// and <see cref="JobicyJobSource"/> as an <see cref="IJobSource"/>. Forces the source's fetch interval in
    /// <see cref="JobFetchOptions"/> to at least <see cref="JobicyOptions.MinInterval"/> (never below one hour).
    /// Returns the HTTP client builder (tests swap the primary handler on it).
    /// </summary>
    public static IHttpClientBuilder AddJobicySource(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JobicyOptions>()
            .Bind(configuration.GetSection(JobicyOptions.SectionName))
            .Validate(o => o.IsValid(),
                "JobFetch:Jobicy: Count must be 1-200, Queries at most 5, each with Geo, Industry or Tag (Tag 3-50 characters), "
                + "DelayBetweenRequests 00:00:00-00:01:00, MinInterval >= 01:00:00 and Timeout > 0.")
            .ValidateOnStart();

        services.AddOptions<JobFetchOptions>()
            .PostConfigure<IOptionsMonitor<JobicyOptions>>((fetch, jobicy) => EnforceMinInterval(fetch, jobicy.CurrentValue));

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IJobSource, JobicyJobSource>();

        return services
            .AddRestEaseClient<IJobicyApi>(BaseAddress.ToString(), new AddRestEaseClientOptions<IJobicyApi>
            {
                // Keep pubDate as the raw string; the source parses it with its offset.
                RestClientConfigurer = client => client.JsonSerializerSettings = new JsonSerializerSettings
                {
                    DateParseHandling = DateParseHandling.None,
                },
            })
            .ConfigureHttpClient((sp, http) =>
            {
                http.BaseAddress = BaseAddress;
                http.Timeout = sp.GetRequiredService<IOptionsMonitor<JobicyOptions>>().CurrentValue.Timeout;
                http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            });
    }

    private static void EnforceMinInterval(JobFetchOptions fetch, JobicyOptions jobicy)
    {
        var key = fetch.Sources.Keys.FirstOrDefault(
            k => string.Equals(k, JobicyJobSource.SourceName, StringComparison.OrdinalIgnoreCase))
            ?? JobicyJobSource.SourceName;

        if (!fetch.Sources.TryGetValue(key, out var source))
            fetch.Sources[key] = source = new JobFetchSourceOptions();

        TimeSpan[] candidates = [source.MinInterval ?? fetch.MinInterval, jobicy.MinInterval, JobicyOptions.MinAllowedInterval];
        source.MinInterval = candidates.Max();
    }
}
