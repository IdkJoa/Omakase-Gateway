using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations;

public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("user_roles");

        builder.HasKey(ur => ur.Id);
        builder.Property(ur => ur.Id)
               .HasColumnName("id");

        builder.Property(ur => ur.UserId)
               .HasColumnName("user_id")
               .IsRequired();

        builder.Property(ur => ur.RoleId)
               .HasColumnName("role_id")
               .IsRequired();

        // The document requires (user_id, role_id) to be unique.
        builder.HasIndex(ur => new { ur.UserId, ur.RoleId })
               .IsUnique()
               .HasDatabaseName("ix_user_roles_user_role");

        builder.Property(ur => ur.AssignedAt)
               .HasColumnName("assigned_at")
               .HasColumnType("timestamptz")
               .IsRequired();
    }
}
