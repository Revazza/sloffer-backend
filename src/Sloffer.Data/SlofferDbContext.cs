using Microsoft.EntityFrameworkCore;

namespace Sloffer.Data;

public sealed class SlofferDbContext(DbContextOptions<SlofferDbContext> options) : DbContext(options)
{
    // DbSets go here as entities are added (one IEntityTypeConfiguration<T> per entity in Configurations/).

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SlofferDbContext).Assembly);
    }
}
