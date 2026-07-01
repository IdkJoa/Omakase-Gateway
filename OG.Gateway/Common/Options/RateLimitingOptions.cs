namespace Application.Common.Options;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";
    public int Limit { get; set; } = 100;
    public int WindowSeconds { get; set; } = 60;
}
