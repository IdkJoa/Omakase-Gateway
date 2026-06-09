using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
               .HasColumnName("id");

        builder.Property(r => r.Name)
               .HasColumnName("name")
               .HasColumnType("varchar(50)")
               .IsRequired();

        builder.HasIndex(r => r.Name)
               .IsUnique()
               .HasDatabaseName("ix_roles_name");

        builder.Property(r => r.Description)
               .HasColumnName("description")
               .HasColumnType("varchar(255)")
               .IsRequired(false);

        builder.Property(r => r.IsActive)
               .HasColumnName("is_active")
               .IsRequired();

        builder.Property(r => r.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        // Relationships 

        builder.HasMany(r => r.UserRoles)
               .WithOne(ur => ur.Role)
               .HasForeignKey(ur => ur.RoleId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
