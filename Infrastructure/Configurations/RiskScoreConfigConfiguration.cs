using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations;

public sealed class RiskScoreConfigConfiguration : IEntityTypeConfiguration<RiskScoreConfig>
{
    public void Configure(EntityTypeBuilder<RiskScoreConfig> builder)
    {
        builder.ToTable("risk_score_config");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
               .HasColumnName("id");

        builder.Property(r => r.PolicyWeight)
               .HasColumnName("policy_weight")
               .HasColumnType("numeric(5,4)")
               .IsRequired();

        builder.Property(r => r.AnomalyWeight)
               .HasColumnName("anomaly_weight")
               .HasColumnType("numeric(5,4)")
               .IsRequired();

        builder.Property(r => r.ColdStartPenalty)
               .HasColumnName("cold_start_penalty")
               .HasColumnType("numeric(5,2)")
               .IsRequired();

        builder.Property(r => r.ColdStartN)
               .HasColumnName("cold_start_n")
               .HasColumnType("integer")
               .IsRequired();

        builder.Property(r => r.BlockThreshold)
               .HasColumnName("block_threshold")
               .HasColumnType("numeric(5,2)")
               .IsRequired();

        builder.Property(r => r.ChallengeThreshold)
               .HasColumnName("challenge_threshold")
               .HasColumnType("numeric(5,2)")
               .IsRequired();

        builder.Property(r => r.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz")
               .IsRequired();
    }
}
