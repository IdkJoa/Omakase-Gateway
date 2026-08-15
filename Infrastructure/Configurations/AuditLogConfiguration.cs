using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    private static string VerdictToString(Verdict v)
    {
           return v switch
           {
                  Verdict.Allow => "ALLOW",
                  Verdict.Challenge => "CHALLENGE",
                  Verdict.Block => "BLOCK",
                  _ => throw new ArgumentOutOfRangeException(nameof(v), v, null)
           };
    }

    private static Verdict StringToVerdict(string v)
    {
           return v switch
           {
                  "ALLOW" => Verdict.Allow,
                  "CHALLENGE" => Verdict.Challenge,
                  "BLOCK" => Verdict.Block,
                  _ => throw new ArgumentOutOfRangeException(nameof(v), v, null)
           };
    }

    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
               .HasColumnName("id");

        builder.Property(a => a.EvaluationId)
               .HasColumnName("evaluation_id")
               .IsRequired();

        builder.HasIndex(a => a.EvaluationId)
               .IsUnique()
               .HasDatabaseName("ix_audit_logs_evaluation_id");

        // Nullable FKs (ON DELETE SET NULL per document)

        builder.Property(a => a.UserId)
               .HasColumnName("user_id")
               .IsRequired(false);

        builder.Property(a => a.ServiceId)
               .HasColumnName("service_id")
               .IsRequired(false);

        #region Request Context

        builder.Property(a => a.SourceIp)
               .HasColumnName("source_ip")
               .HasColumnType("varchar(45)")
               .IsRequired();

        builder.Property(a => a.Geo)
               .HasColumnName("geo")
               .HasColumnType("jsonb")
               .IsRequired(false);

        builder.Property(a => a.UserAgent)
               .HasColumnName("user_agent")
               .HasColumnType("text")
               .IsRequired(false);

        builder.Property(a => a.FingerprintHash)
               .HasColumnName("fingerprint_hash")
               .HasColumnType("varchar(64)")
               .IsRequired(false);

        #endregion

        #region Risk Scores

        builder.Property(a => a.PolicyScore)
               .HasColumnName("policy_score")
               .HasColumnType("numeric(5,2)")
               .IsRequired();

        builder.Property(a => a.AnomalyScore)
               .HasColumnName("anomaly_score")
               .HasColumnType("numeric(5,2)")
               .IsRequired();

        builder.Property(a => a.RiskScore)
               .HasColumnName("risk_score")
               .HasColumnType("numeric(5,2)")
               .IsRequired();

        builder.Property(a => a.Verdict)
               .HasColumnName("verdict")
               .HasColumnType("varchar(10)")
               .HasConversion(v => VerdictToString(v), v => StringToVerdict(v))
               .IsRequired();

        #endregion
        
        // GIN index for efficient JSONB queries on triggered_rules (high-volume table).
        builder.Property(a => a.TriggeredRules)
               .HasColumnName("triggered_rules")
               .HasColumnType("jsonb")
               .IsRequired(false);

        builder.HasIndex(a => a.TriggeredRules)
               .HasMethod("GIN")
               .HasDatabaseName("ix_audit_logs_triggered_rules_gin");

        builder.Property(a => a.EvaluatedAt)
               .HasColumnName("evaluated_at")
               .HasColumnType("timestamptz")
               .IsRequired();

        // Index to speed up time-range queries on the highest-volume table.
        builder.HasIndex(a => a.EvaluatedAt)
               .HasDatabaseName("ix_audit_logs_evaluated_at");

        #region Relationships (FK navigation configured from the User/ProtectedService side)

        builder.HasOne(a => a.ProtectedService)
               .WithMany(s => s.AuditLogs)
               .HasForeignKey(a => a.ServiceId)
               .OnDelete(DeleteBehavior.SetNull);

        #endregion 
    }
}
