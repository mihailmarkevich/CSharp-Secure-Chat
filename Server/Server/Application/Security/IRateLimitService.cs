using Server.Domain.Security;

namespace Server.Application.Security
{
    public interface IRateLimitService
    {
        /// <summary>
        /// Register an action for the given IP.
        /// Returns true if allowed (limit not exceeded),
        /// false if this action should be treated as spam.
        /// </summary>
        bool RegisterAction(string ip, ChatAction action);
    }
}
