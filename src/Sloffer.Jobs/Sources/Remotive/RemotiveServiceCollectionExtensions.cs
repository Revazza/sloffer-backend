using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using RestEase.HttpClientFactory;
using Sloffer.Jobs.Fetching;
using Sloffer.Jobs.Sources.Arbeitnow;

namespace Sloffer.Jobs.Sources.Remotive;

public static class RemotiveServiceCollectionExtensions
{
    public static readonly Uri BaseAddress = new("https://remotive.com/");

    /// <summary>
    /// Registers <see cref="RemotiveOptions"/> (<c>JobFetch:Remotive</c>), the <see cref="IRemotiveClient"/> RestEase
    /// client and <see cref="RemotiveJobSource"/> as an <see cref="IJobSource"/>. Its fetch interval comes from
    /// <c>JobFetch:Sources:Remotive:MinInterval</c> (6 hours in appsettings, as Remotive asks for few calls a day).
    /// Returns the HTTP client builder (tests swap the primary handler on it).
    /// </summary>
    public static IHttpClientBuilder AddRemotiveSource(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RemotiveOptions>()
            .Bind(configuration.GetSection(RemotiveOptions.SectionName))
            .Validate(o => o.IsValid(), "JobFetch:Remotive: Category must be set and Timeout > 0.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IJobSource, RemotiveJobSource>();

        return services
            .AddRestEaseClient<IRemotiveClient>(BaseAddress.ToString(), new AddRestEaseClientOptions<IRemotiveClient>
            {
                RestClientConfigurer = client => client.JsonSerializerSettings = new JsonSerializerSettings
                {
                    ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
                },
            })
            .ConfigureHttpClient((sp, http) =>
            {
                http.BaseAddress = BaseAddress;
                http.Timeout = sp.GetRequiredService<IOptionsMonitor<RemotiveOptions>>().CurrentValue.Timeout;
                http.DefaultRequestHeaders.UserAgent.ParseAdd(ArbeitnowServiceCollectionExtensions.UserAgent);
            });
    }
}
