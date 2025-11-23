namespace Server.Domain.Chat
{
    public class ChatMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string ConnectionId { get; set; } = string.Empty;
        public string UserName { get; set; } = default!;
        public string Text { get; set; } = default!;
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    }

}
