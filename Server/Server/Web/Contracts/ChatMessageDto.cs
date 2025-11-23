namespace Server.Web.Contracts
{
    public sealed class ChatMessageDto
    {
        public Guid Id { get; set; }
        public string ConnectionId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public DateTimeOffset Timestamp { get; set; }
    }
}
