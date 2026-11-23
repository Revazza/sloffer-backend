using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sloffer.Data;

public static class DataServiceCollectionExtensions
{
    public const string ConnectionStringName = "Sloffer";

    /// <summary>Registers <see cref="SlofferDbContext"/> using the "Sloffer" connection string.</summary>
    public static IServiceCollection AddSlofferData(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is missing. Run scripts/setup-local-db.sh " +
                $"or set ConnectionStrings__{ConnectionStringName}.");

        services.AddDbContext<SlofferDbContext>(options => options.UseNpgsql(connectionString));
        return services;
    }
}
