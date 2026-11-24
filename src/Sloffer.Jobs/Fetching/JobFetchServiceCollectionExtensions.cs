using Microsoft.Extensions.DependencyInjection.Extensions;
using Sloffer.Data;
using Sloffer.Jobs.Dedup;
using Sloffer.Jobs.Matching;

namespace Sloffer.Jobs.Fetching;

public static class JobFetchServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="JobFetchOptions"/> (section <c>JobFetch</c>), <see cref="JobFetcher"/>, job scoring
    /// (section <c>JobScoring</c>), the optional startup <see cref="JobDedupBackfillService"/> and
    /// <see cref="JobRescoreService"/>, and the
    /// <see cref="JobFetchService"/> worker. Sources are registered separately as <see cref="IJobSource"/>.
    /// </summary>
    public static IServiceCollection AddJobFetching(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JobFetchOptions>()
            .Bind(configuration.GetSection(JobFetchOptions.SectionName))
            .Validate(o => o.IsValid(), "JobFetch: CycleDelay must be > 0 and every MinInterval >= 0.")
            .Validate(o => JobAgeWindow.IsValid(o.MaxAgeDays), JobAgeWindow.ValidationMessage)
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddJobScoring(configuration);
        services.AddSingleton<JobFetcher>();
        // Hosted services start in registration order: the optional backfill and rescore finish before fetching begins.
        services.AddHostedService<JobDedupBackfillService>();
        services.AddHostedService<JobRescoreService>();
        services.AddHostedService<JobFetchService>();
        return services;
    }
}
