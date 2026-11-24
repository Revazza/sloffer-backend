using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sloffer.Jobs.Matching;

public static class JobScoringServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="JobScoringOptions"/> (section <c>JobScoring</c>) and the <see cref="JobScorer"/> singleton.
    /// Safe to call more than once. The startup rescore is registered by <c>AddJobFetching</c>.
    /// </summary>
    public static IServiceCollection AddJobScoring(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(s => s.ServiceType == typeof(JobScorer)))
            return services;

        services.AddOptions<JobScoringOptions>()
            .Bind(configuration.GetSection(JobScoringOptions.SectionName))
            .Validate(o => o.IsValid(),
                "JobScoring: RescoreBatchSize must be > 0, BaseScore 0-100, StackPointsMax >= 0, GapPointsMin <= 0 and FewYearsMax < ManyYearsMin.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<JobScorer>();
        return services;
    }
}
