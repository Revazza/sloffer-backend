using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Sloffer.Data.Configurations;

public sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.HasKey(j => j.Id);

        builder.Property(j => j.Source).HasMaxLength(100).IsRequired();
        builder.Property(j => j.ExternalId).HasMaxLength(256).IsRequired();
        builder.Property(j => j.Url).IsRequired();
        builder.Property(j => j.Title).IsRequired();
        builder.Property(j => j.Company).IsRequired();
        builder.Property(j => j.PostedAt).IsRequired();
        builder.Property(j => j.FetchedAt).IsRequired();

        builder.HasIndex(j => new { j.Source, j.ExternalId }).IsUnique();
        builder.HasIndex(j => j.PostedAt);
    }
}
