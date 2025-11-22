namespace Server.Domain.Security
{
    public readonly record struct RateLimitRule(TimeSpan Window, int Limit);
}
