namespace Infrastructure.Workers;

public sealed class AuditWorkerOptions
{   
    public const string SectionName = "AuditWorker"; 
    public int BatchSize { get; set; } = 50;
    public int RetryDelaySeconds { get; set; } = 5;
}
