namespace Server.Application.Chat
{
    public interface IConnectionRegistry
    {
        bool TryRegisterConnection(string connectionId, string ip, out int activeConnections);
        void UnregisterConnection(string connectionId);

        bool TryGetIp(string connectionId, out string? ip);

        bool TryGetUserName(string connectionId, out string? userName);

        /// <summary>
        /// Attempts to change username for given connection.
        /// Returns true if name changed, false if ignored (same name).
        /// Throws if name is already taken by another connection.
        /// </summary>
        bool ChangeUserName(string connectionId, string newName, out string? oldName);

        IReadOnlyDictionary<string, string> GetCurrentUsersSnapshot();
    }

}
