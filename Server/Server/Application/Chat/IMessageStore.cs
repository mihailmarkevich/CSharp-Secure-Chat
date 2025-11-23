using Server.Domain.Chat;

namespace Server.Application.Chat
{
    public interface IMessageStore
    {
        Task AddAsync(ChatMessage message);
        Task<IReadOnlyList<ChatMessage>> GetLastAsync(int count);
        Task UpdateUserNameAsync(string connectionId, string newUserName);
    }
}
