using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sloffer.Jobs.Fetching;

public static class JobFetchServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="JobFetchOptions"/> (section <c>JobFetch</c>), <see cref="JobFetcher"/> and the
    /// <see cref="JobFetchService"/> worker. Sources are registered separately as <see cref="IJobSource"/>.
    /// </summary>
    public static IServiceCollection AddJobFetching(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JobFetchOptions>()
            .Bind(configuration.GetSection(JobFetchOptions.SectionName))
            .Validate(o => o.IsValid(), "JobFetch: CycleDelay must be > 0 and every MinInterval >= 0.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<JobFetcher>();
        services.AddHostedService<JobFetchService>();
        return services;
    }
}
