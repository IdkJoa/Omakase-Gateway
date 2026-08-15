using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations;

public sealed class ServicePolicyConfiguration : IEntityTypeConfiguration<ServicePolicy>
{
    public void Configure(EntityTypeBuilder<ServicePolicy> builder)
    {
        builder.ToTable("service_policies");

        builder.HasKey(sp => sp.Id);
        builder.Property(sp => sp.Id)
               .HasColumnName("id");

        builder.Property(sp => sp.ServiceId)
               .HasColumnName("service_id")
               .IsRequired();

        builder.Property(sp => sp.PolicyId)
               .HasColumnName("policy_id")
               .IsRequired();

        // The document requires (service_id, policy_id) to be unique.
        builder.HasIndex(sp => new { sp.ServiceId, sp.PolicyId })
               .IsUnique()
               .HasDatabaseName("ix_service_policies_service_policy");

        builder.Property(sp => sp.IsEnabled)
               .HasColumnName("is_enabled")
               .IsRequired();

        builder.HasOne(sp => sp.AccessPolicy)
               .WithMany(p => p.ServicePolicies)
               .HasForeignKey(sp => sp.PolicyId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
