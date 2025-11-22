namespace Server.Application.Options
{
    public class RateLimitRuleOptions
    {
        public int WindowSeconds { get; set; } = 5;
        public int Limit { get; set; } = 5;

        public TimeSpan GetWindow() => TimeSpan.FromSeconds(WindowSeconds);
    }

}
