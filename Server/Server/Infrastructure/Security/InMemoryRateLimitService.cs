using Microsoft.Extensions.Options;
using Server.Application.Options;
using Server.Application.Security;
using Server.Domain.Security;
using System.Collections.Concurrent;

namespace Server.Infrastructure.Security
{
    public sealed class InMemoryRateLimitService : IRateLimitService
    {
        private sealed class RateLimitState
        {
            public int Count;
            public DateTimeOffset WindowStart;
        }

        private readonly IOptionsMonitor<RateLimitOptions> _optionsMonitor;
        private readonly ConcurrentDictionary<(string ip, ChatAction action), RateLimitState> _states = new();

        public InMemoryRateLimitService(IOptionsMonitor<RateLimitOptions> optionsMonitor)
        {
            _optionsMonitor = optionsMonitor;
        }

        public bool RegisterAction(string ip, ChatAction action)
        {
            if (string.IsNullOrWhiteSpace(ip))
                ip = "unknown";

            var options = _optionsMonitor.CurrentValue;
            var rule = options.ToRule(action);

            var now = DateTimeOffset.UtcNow;
            var key = (ip, action);

            var state = _states.GetOrAdd(key, _ => new RateLimitState
            {
                Count = 0,
                WindowStart = now
            });

            lock (state)
            {
                if (now - state.WindowStart > rule.Window)
                {
                    state.WindowStart = now;
                    state.Count = 0;
                }

                state.Count++;
                return state.Count <= rule.Limit;
            }
        }
    }

}
