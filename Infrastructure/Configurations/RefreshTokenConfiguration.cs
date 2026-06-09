using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
               .HasColumnName("id");

        #region Properties

        builder.Property(t => t.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(t => t.TokenHash)
            .HasColumnName("token_hash")
            .HasColumnType("varchar(255)")
            .IsRequired();

        builder.HasIndex(t => t.TokenHash)
            .IsUnique()
            .HasDatabaseName("ix_refresh_tokens_token_hash");

        builder.Property(t => t.DeviceInfo)
            .HasColumnName("device_info")
            .HasColumnType("varchar(255)")
            .IsRequired(false);

        builder.Property(t => t.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(t => t.IsRevoked)
            .HasColumnName("is_revoked")
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();
        
        #endregion
        
        // Partial index: quickly find all active (non-revoked, non-expired) tokens for a user.
        builder.HasIndex(t => new { t.UserId, t.IsRevoked })
               .HasFilter("is_revoked = false")
               .HasDatabaseName("ix_refresh_tokens_user_active");
    }
}
