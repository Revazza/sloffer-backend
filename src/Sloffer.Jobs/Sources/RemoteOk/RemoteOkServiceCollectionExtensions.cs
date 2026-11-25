using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using RestEase.HttpClientFactory;
using Sloffer.Jobs.Fetching;
using Sloffer.Jobs.Sources.Arbeitnow;

namespace Sloffer.Jobs.Sources.RemoteOk;

public static class RemoteOkServiceCollectionExtensions
{
    public static readonly Uri BaseAddress = new("https://remoteok.com/");

    /// <summary>
    /// Registers <see cref="RemoteOkOptions"/> (<c>JobFetch:RemoteOk</c>), the <see cref="IRemoteOkClient"/> RestEase
    /// client and <see cref="RemoteOkJobSource"/> as an <see cref="IJobSource"/>. Its fetch interval comes from
    /// <c>JobFetch:Sources:Remote OK:MinInterval</c> (6 hours in appsettings). Returns the HTTP client builder (tests swap
    /// the primary handler on it).
    /// </summary>
    public static IHttpClientBuilder AddRemoteOkSource(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RemoteOkOptions>()
            .Bind(configuration.GetSection(RemoteOkOptions.SectionName))
            .Validate(o => o.IsValid(), "JobFetch:RemoteOk: Timeout must be > 0.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IJobSource, RemoteOkJobSource>();

        return services
            .AddRestEaseClient<IRemoteOkClient>(BaseAddress.ToString())
            .ConfigureHttpClient((sp, http) =>
            {
                http.BaseAddress = BaseAddress;
                http.Timeout = sp.GetRequiredService<IOptionsMonitor<RemoteOkOptions>>().CurrentValue.Timeout;
                // Remote OK rejects requests without a User-Agent.
                http.DefaultRequestHeaders.UserAgent.ParseAdd(ArbeitnowServiceCollectionExtensions.UserAgent);
            });
    }
}
