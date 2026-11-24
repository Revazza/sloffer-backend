using Microsoft.EntityFrameworkCore;

namespace Sloffer.Data;

public sealed class SlofferDbContext(DbContextOptions<SlofferDbContext> options) : DbContext(options)
{
    // One IEntityTypeConfiguration<T> per entity in Configurations/.
    public DbSet<Job> Jobs => Set<Job>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SlofferDbContext).Assembly);
    }
}
