using Server.Domain.Security;

namespace Server.Application.Options
{
    public class RateLimitOptions
    {
        public RateLimitRuleOptions Connect { get; set; } = new();
        public RateLimitRuleOptions ChangeName { get; set; } = new();
        public RateLimitRuleOptions SendMessage { get; set; } = new();
        public RateLimitRuleOptions GetHistory { get; set; } = new();

        public RateLimitRule ToRule(ChatAction action) => action switch
        {
            ChatAction.Connect => new RateLimitRule(Connect.GetWindow(), Connect.Limit),
            ChatAction.ChangeName => new RateLimitRule(ChangeName.GetWindow(), ChangeName.Limit),
            ChatAction.SendMessage => new RateLimitRule(SendMessage.GetWindow(), SendMessage.Limit),
            ChatAction.GetHistory => new RateLimitRule(GetHistory.GetWindow(), GetHistory.Limit),
            _ => new RateLimitRule(SendMessage.GetWindow(), SendMessage.Limit)
        };
    }
}
