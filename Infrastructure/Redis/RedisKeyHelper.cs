namespace Infrastructure.Redis;

public static class RedisKeyHelper
{
    public static string GetRateLimitKey(string ip) => $"ratelimit:{ip}";

    public static string GetBlacklistKey(string jti) => $"blacklist:{jti}";

    public static string GetProfileKey(string userId) => $"profile:{userId}";

    public static string GetFingerprintKey(string userId) => $"fingerprint:{userId}";
}
