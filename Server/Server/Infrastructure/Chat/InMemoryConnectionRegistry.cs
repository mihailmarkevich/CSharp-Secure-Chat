using Server.Application.Chat;
using System.Collections.Concurrent;

namespace Server.Infrastructure.Chat
{
    /// <summary>
    /// Process-local in-memory registry.
    /// NOTE: Not suitable for multi-instance deployments.
    /// </summary>
    public sealed class InMemoryConnectionRegistry : IConnectionRegistry
    {
        private readonly int _maxConnectionsPerIp;

        // connectionId -> userName
        private readonly ConcurrentDictionary<string, string> _userNames = new();

        // userName -> connectionId (case-insensitive)
        private readonly ConcurrentDictionary<string, string> _nameOwners =
            new(StringComparer.OrdinalIgnoreCase);

        // connectionId -> ip
        private readonly ConcurrentDictionary<string, string> _connectionIps = new();

        // ip -> active connection count
        private readonly ConcurrentDictionary<string, int> _ipConnectionCounts = new();

        public InMemoryConnectionRegistry(int maxConnectionsPerIp)
        {
            _maxConnectionsPerIp = maxConnectionsPerIp;
        }

        public bool TryRegisterConnection(string connectionId, string ip, out int activeConnections)
        {
            _connectionIps[connectionId] = ip;

            while (true)
            {
                var current = _ipConnectionCounts.GetOrAdd(ip, 0);
                if (current >= _maxConnectionsPerIp)
                {
                    activeConnections = current;
                    
                    _connectionIps.TryRemove(connectionId, out _);
                    return false;
                }

                var newValue = current + 1;
                if (_ipConnectionCounts.TryUpdate(ip, newValue, current))
                {
                    activeConnections = newValue;
                    return true;
                }

                // if somebody updated in parallel - try again
            }
        }

        public void UnregisterConnection(string connectionId)
        {
            if (_connectionIps.TryRemove(connectionId, out var ip))
            {
                var newCount = _ipConnectionCounts.AddOrUpdate(
                    ip,
                    _ => 0,
                    (_, current) => Math.Max(0, current - 1));

                if (newCount == 0)
                {
                    _ipConnectionCounts.TryRemove(ip, out _);
                }
            }

            if (_userNames.TryRemove(connectionId, out var name))
            {
                if (!string.IsNullOrEmpty(name) &&
                    _nameOwners.TryGetValue(name, out var ownerId) &&
                    ownerId == connectionId)
                {
                    _nameOwners.TryRemove(name, out _);
                }
            }
        }

        public bool TryGetIp(string connectionId, out string? ip)
        {
            if (_connectionIps.TryGetValue(connectionId, out var stored))
            {
                ip = stored;
                return true;
            }

            ip = null;
            return false;
        }

        public bool TryGetUserName(string connectionId, out string? userName)
        {
            if (_userNames.TryGetValue(connectionId, out var name))
            {
                userName = name;
                return true;
            }

            userName = null;
            return false;
        }

        public bool ChangeUserName(string connectionId, string newName, out string? oldName)
        {
            _userNames.TryGetValue(connectionId, out oldName);

            if (!string.IsNullOrEmpty(oldName) && string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!_nameOwners.TryAdd(newName, connectionId))
            {
                if (_nameOwners.TryGetValue(newName, out var existingOwner) &&
                    existingOwner != connectionId)
                {
                    throw new InvalidOperationException("User name is already taken.");
                }
            }

            // free previous name
            if (!string.IsNullOrEmpty(oldName) &&
                !string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
            {
                _nameOwners.TryRemove(oldName, out _);
            }

            _userNames[connectionId] = newName;
            return true;
        }

        public IReadOnlyDictionary<string, string> GetCurrentUsersSnapshot()
            => new Dictionary<string, string>(_userNames);
    }
}
