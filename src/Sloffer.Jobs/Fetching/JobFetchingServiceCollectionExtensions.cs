namespace Sloffer.Jobs.Fetching;

public static class JobFetchingServiceCollectionExtensions
{
    /// <summary>Registers the fetch worker. Sources are added separately as <c>IJobSource</c> services.</summary>
    public static IServiceCollection AddJobFetching(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JobFetchOptions>(configuration.GetSection(JobFetchOptions.SectionName));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<JobFetcher>();
        services.AddHostedService<JobFetchService>();
        return services;
    }
}
