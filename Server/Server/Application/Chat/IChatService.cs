using Server.Web.Contracts;

namespace Server.Application.Chat
{
    public interface IChatService
    {
        Task ChangeUserNameAsync(string connectionId, string newName, string ip);
        Task<ChatMessageDto> SendMessageAsync(string connectionId, string text, string ip);
        Task<IReadOnlyList<ChatMessageDto>> GetHistoryAsync(string connectionId, int count, string ip);
    }
}
