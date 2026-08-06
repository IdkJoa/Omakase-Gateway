using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations;

public sealed class ProtectedServiceConfiguration : IEntityTypeConfiguration<ProtectedService>
{
    public void Configure(EntityTypeBuilder<ProtectedService> builder)
    {
        builder.ToTable("protected_services");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
               .HasColumnName("id");

        #region Properties

        builder.Property(s => s.Name)
            .HasColumnName("name")
            .HasColumnType("varchar(150)")
            .IsRequired();

        builder.Property(s => s.UpstreamUrl)
            .HasColumnName("upstream_url")
            .HasColumnType("varchar(500)")
            .IsRequired();

        builder.Property(s => s.RequiresAuth)
            .HasColumnName("requires_auth")
            .IsRequired();

        builder.Property(s => s.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(s => s.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired(false);

        #endregion
        
        #region Relationships
        
        builder.HasMany(s => s.ServicePolicies)
               .WithOne(sp => sp.ProtectedService)
               .HasForeignKey(sp => sp.ServiceId)
               .OnDelete(DeleteBehavior.Cascade);        

        #endregion
    }
}
