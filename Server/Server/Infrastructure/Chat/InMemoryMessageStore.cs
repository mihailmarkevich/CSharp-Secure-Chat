using Server.Application.Chat;
using Server.Domain.Chat;
using System.Collections.Concurrent;

namespace Server.Infrastructure.Chat
{
    public sealed class InMemoryMessageStore : IMessageStore
    {
        private readonly ConcurrentQueue<ChatMessage> _messages = new();
        private long _lastId = 0;

        public Task AddAsync(ChatMessage message)
        {
            message.Id = Interlocked.Increment(ref _lastId);
            _messages.Enqueue(message);
            // Optional: you may want to cap the queue size.
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
    }
}
