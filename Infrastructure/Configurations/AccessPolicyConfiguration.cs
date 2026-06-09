using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations;

public sealed class AccessPolicyConfiguration : IEntityTypeConfiguration<AccessPolicy>
{
    private static string PolicyTypeToString(PolicyType v)
    {
           return v switch
           {
                  PolicyType.Geofence => "GEOFENCE",
                  PolicyType.TimeWindow => "TIME_WINDOW",
                  PolicyType.Fingerprint => "FINGERPRINT",
                  PolicyType.ImpossibleTravel => "IMPOSSIBLE_TRAVEL",
                  _ => throw new ArgumentOutOfRangeException(nameof(v), v, null)
           };
    }

    private static PolicyType StringToPolicyType(string v)
    {
           return v switch
           {
                  "GEOFENCE" => PolicyType.Geofence,
                  "TIME_WINDOW" => PolicyType.TimeWindow,
                  "FINGERPRINT" => PolicyType.Fingerprint,
                  "IMPOSSIBLE_TRAVEL" => PolicyType.ImpossibleTravel,
                  _ => throw new ArgumentOutOfRangeException(nameof(v), v, null)
           };
    }

    public void Configure(EntityTypeBuilder<AccessPolicy> builder)
    {
        builder.ToTable("access_policies");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
               .HasColumnName("id");

        builder.Property(p => p.Name)
               .HasColumnName("name")
               .HasColumnType("varchar(150)")
               .IsRequired();

        builder.Property(p => p.Type)
               .HasColumnName("type")
               .HasColumnType("varchar(30)")
               .HasConversion(v => PolicyTypeToString(v), v => StringToPolicyType(v))
               .IsRequired();

        builder.Property(p => p.Config)
               .HasColumnName("config")
               .HasColumnType("jsonb")
               .IsRequired();

        builder.Property(p => p.Weight)
               .HasColumnName("weight")
               .HasColumnType("numeric(4,3)")
               .IsRequired();

        builder.Property(p => p.IsActive)
               .HasColumnName("is_active")
               .IsRequired();

        builder.Property(p => p.CreatedById)
               .HasColumnName("created_by")
               .IsRequired();

        builder.Property(p => p.CreatedAt)
               .HasColumnName("created_at")
               .HasColumnType("timestamptz")
               .IsRequired();
    }
}
