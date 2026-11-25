using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using RestEase.HttpClientFactory;
using Sloffer.Jobs.Fetching;

namespace Sloffer.Jobs.Sources.Arbeitnow;

public static class ArbeitnowServiceCollectionExtensions
{
    public static readonly Uri BaseAddress = new("https://www.arbeitnow.com/");

    public const string UserAgent = "Sloffer/1.0 (personal job aggregator; +https://github.com/revazza/sloffer-backend)";

    /// <summary>
    /// Registers <see cref="ArbeitnowOptions"/> (<c>JobFetch:Arbeitnow</c>), the <see cref="IArbeitnowClient"/>
    /// RestEase client and <see cref="ArbeitnowJobSource"/> as an <see cref="IJobSource"/>. Its fetch interval comes
    /// from <see cref="JobFetchOptions"/> (<c>JobFetch:Sources:Arbeitnow:MinInterval</c>, else the global one).
    /// Returns the HTTP client builder (tests swap the primary handler on it).
    /// </summary>
    public static IHttpClientBuilder AddArbeitnowSource(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ArbeitnowOptions>()
            .Bind(configuration.GetSection(ArbeitnowOptions.SectionName))
            .Validate(o => o.IsValid(),
                "JobFetch:Arbeitnow: MaxPages must be 1-20, DelayBetweenRequests 00:00:00-00:01:00 and Timeout > 0.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IJobSource, ArbeitnowJobSource>();

        return services
            .AddRestEaseClient<IArbeitnowClient>(BaseAddress.ToString(), new AddRestEaseClientOptions<IArbeitnowClient>
            {
                RestClientConfigurer = client => client.JsonSerializerSettings = new JsonSerializerSettings
                {
                    ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
                },
            })
            .ConfigureHttpClient((sp, http) =>
            {
                http.BaseAddress = BaseAddress;
                http.Timeout = sp.GetRequiredService<IOptionsMonitor<ArbeitnowOptions>>().CurrentValue.Timeout;
                http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            });
    }
}
