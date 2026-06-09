using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations;

public sealed class UserBehaviorProfileConfiguration : IEntityTypeConfiguration<UserBehaviorProfile>
{
    public void Configure(EntityTypeBuilder<UserBehaviorProfile> builder)
    {
        builder.ToTable("user_behavior_profiles");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
               .HasColumnName("id");

        builder.Property(p => p.UserId)
               .HasColumnName("user_id")
               .IsRequired();

        // Enforces the 1:1 relationship with users at the database level.
        builder.HasIndex(p => p.UserId)
               .IsUnique()
               .HasDatabaseName("ix_user_behavior_profiles_user_id");

        builder.Property(p => p.FeatureVector)
               .HasColumnName("feature_vector")
               .HasColumnType("jsonb")
               .IsRequired(false);

        builder.Property(p => p.AccessCount)
               .HasColumnName("access_count")
               .IsRequired();

        builder.Property(p => p.IsColdStart)
               .HasColumnName("is_cold_start")
               .IsRequired();

        builder.Property(p => p.BaseRiskPenalty)
               .HasColumnName("base_risk_penalty")
               .HasColumnType("numeric(5,2)")
               .IsRequired();

        builder.Property(p => p.LastTrainedAt)
               .HasColumnName("last_trained_at")
               .HasColumnType("timestamptz")
               .IsRequired(false);
    }
}
