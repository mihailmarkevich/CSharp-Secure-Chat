using Server.Application.Security;
using System.Collections.Concurrent;

namespace Server.Infrastructure.Security
{
    public sealed class InMemoryBanService : IBanService
    {
        private readonly ConcurrentDictionary<string, DateTimeOffset> _bans = new();

        public bool IsBanned(string ip, out TimeSpan? remaining)
        {
            remaining = null;

            if (string.IsNullOrWhiteSpace(ip))
                return false;

            if (!_bans.TryGetValue(ip, out var until))
                return false;

            var now = DateTimeOffset.UtcNow;
            if (until <= now)
            {
                _bans.TryRemove(ip, out _);
                return false;
            }

            var rem = until - now;
            remaining = rem;
            return true;
        }

        public DateTimeOffset Ban(string ip, TimeSpan duration)
        {
            var until = DateTimeOffset.UtcNow + duration;
            _bans[ip] = until;
            return until;
        }
    }
}
