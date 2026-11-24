using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Sloffer.Data.Configurations;

public sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.HasKey(job => job.Id);

        builder.Property(job => job.Source).IsRequired();
        builder.Property(job => job.ExternalId).IsRequired();
        builder.Property(job => job.Url).IsRequired();
        builder.Property(job => job.Title).IsRequired();
        builder.Property(job => job.Company).IsRequired();
        builder.Property(job => job.PostedAt).IsRequired();
        builder.Property(job => job.FetchedAt).IsRequired();

        builder.HasIndex(job => new { job.Source, job.ExternalId }).IsUnique();
        builder.HasIndex(job => job.PostedAt);
    }
}
