using Server.Application.Chat;
using Server.Domain.Chat;
using System.Collections.Concurrent;

namespace Server.Infrastructure.Chat
{
    public sealed class InMemoryMessageStore : IMessageStore
    {
        private readonly ConcurrentQueue<ChatMessage> _messages = new();

        public Task AddAsync(ChatMessage message)
        {
            if (message.Id == Guid.Empty)
            {
                message.Id = Guid.NewGuid();
            }

            _messages.Enqueue(message);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ChatMessage>> GetLastAsync(int count)
        {
            if (count <= 0)
                count = 1;

            var array = _messages.ToArray();
            if (array.Length == 0)
                return Task.FromResult<IReadOnlyList<ChatMessage>>(Array.Empty<ChatMessage>());

            var sliced = array
                .Skip(Math.Max(0, array.Length - count))
                .ToArray();

            return Task.FromResult<IReadOnlyList<ChatMessage>>(sliced);
        }

        public Task UpdateUserNameAsync(string connectionId, string newUserName)
        {
            foreach (var msg in _messages)
            {
                if (string.Equals(msg.ConnectionId, connectionId, StringComparison.Ordinal))
                {
                    msg.UserName = newUserName;
                }
            }

            return Task.CompletedTask;
        }
    }

}
