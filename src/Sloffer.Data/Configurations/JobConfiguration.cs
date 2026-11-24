using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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

        builder.Property(j => j.ScoreReasons)
            .HasColumnType("jsonb")
            .HasConversion(
                reasons => reasons == null ? null : JsonSerializer.Serialize(reasons, JsonOptions),
                json => json == null ? null : JsonSerializer.Deserialize<List<string>>(json, JsonOptions),
                new ValueComparer<List<string>?>(
                    (a, b) => a == null ? b == null : b != null && a.SequenceEqual(b),
                    reasons => reasons == null ? 0 : reasons.Aggregate(0, (hash, r) => HashCode.Combine(hash, r.GetHashCode())),
                    reasons => reasons == null ? null : reasons.ToList()));

        builder.HasIndex(j => new { j.Source, j.ExternalId }).IsUnique();
        builder.HasIndex(j => j.PostedAt);
        builder.HasIndex(j => j.Score);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);
}
