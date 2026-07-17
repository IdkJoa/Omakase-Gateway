using Domain.Entities;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id)
               .HasColumnName("id");

        builder.Property(u => u.Username)
               .HasColumnName("username")
               .HasColumnType("varchar(150)")
               .IsRequired();

        builder.HasIndex(u => u.Username)
               .IsUnique()
               .HasDatabaseName("ix_users_username");

        builder.Property(u => u.Type)
               .HasColumnName("user_type")
               .HasColumnType("varchar(20)")
               .HasConversion(
                   v => v == UserType.SecurityOfficer ? "SECURITY_OFFICER" : "CLIENT_USER",
                   v => v == "SECURITY_OFFICER" ? UserType.SecurityOfficer : UserType.Client)
               .IsRequired();

        builder.Property(u => u.PasswordHash)
               .HasColumnName("password_hash")
               .HasColumnType("varchar(255)")
               .IsRequired(false);

        builder.Property(u => u.KeycloakSub)
               .HasColumnName("keycloak_sub")
               .HasColumnType("varchar(255)")
               .IsRequired(false);

        builder.HasIndex(u => u.KeycloakSub)
               .IsUnique()
               .HasFilter("keycloak_sub IS NOT NULL")
               .HasDatabaseName("ix_users_keycloak_sub");

        builder.Property(u => u.IsActive)
               .HasColumnName("is_active")
               .IsRequired();

        builder.Property(u => u.FailedAttempts)
               .HasColumnName("failed_attempts")
               .IsRequired();

        builder.Property(u => u.LockedUntil)
               .HasColumnName("locked_until")
               .HasColumnType("timestamptz")
               .IsRequired(false);

        builder.Property(u => u.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        builder.Property(u => u.UpdatedAt)
               .HasColumnName("updated_at")
               .HasColumnType("timestamptz")
               .IsRequired(false);

        // HU-046 / T-102: step-up MFA (TOTP) — SRS §7.1
        builder.Property(u => u.TotpSecret)
               .HasColumnName("totp_secret")
               .HasColumnType("varchar(255)")
               .IsRequired(false);

        builder.Property(u => u.MfaEnabled)
               .HasColumnName("mfa_enabled")
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(u => u.IsInteractive)
               .HasColumnName("is_interactive")
               .HasDefaultValue(true)
               .IsRequired();

        #region Relationships

        builder.HasOne(u => u.BehaviorProfile)
               .WithOne(p => p.User)
               .HasForeignKey<UserBehaviorProfile>(p => p.UserId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(u => u.RefreshTokens)
               .WithOne(t => t.User)
               .HasForeignKey(t => t.UserId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(u => u.AuditLogs)
               .WithOne(a => a.User)
               .HasForeignKey(a => a.UserId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(u => u.CreatedPolicies)
               .WithOne(p => p.CreatedBy)
               .HasForeignKey(p => p.CreatedById)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(u => u.UserRoles)
               .WithOne(ur => ur.User)
               .HasForeignKey(ur => ur.UserId)
               .OnDelete(DeleteBehavior.Cascade);

        #endregion
    }
}
