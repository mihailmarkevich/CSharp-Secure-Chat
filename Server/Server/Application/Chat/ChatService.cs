using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Server.Application.Options;
using Server.Application.Security;
using Server.Domain.Chat;
using Server.Domain.Security;
using Server.Helpers;
using Server.Web.Contracts;

namespace Server.Application.Chat
{
    public sealed class ChatService : IChatService
    {
        private readonly IMessageStore _messageStore;
        private readonly IConnectionRegistry _registry;
        private readonly IBanService _banService;
        private readonly IRateLimitService _rateLimitService;
        private readonly TimeSpan _banDuration;
        private readonly ILogger<ChatService> _logger;

        public ChatService(
            IMessageStore messageStore,
            IConnectionRegistry registry,
            IBanService banService,
            IRateLimitService rateLimitService,
            IOptions<SpamProtectionOptions> spamOptions,
            ILogger<ChatService> logger)
        {
            _messageStore = messageStore ?? throw new ArgumentNullException(nameof(messageStore));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _banService = banService ?? throw new ArgumentNullException(nameof(banService));
            _rateLimitService = rateLimitService ?? throw new ArgumentNullException(nameof(rateLimitService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (spamOptions is null) throw new ArgumentNullException(nameof(spamOptions));
            var opts = spamOptions.Value;
            _banDuration = TimeSpan.FromSeconds(opts.BanDurationSeconds);
        }

        public async Task ChangeUserNameAsync(string connectionId, string newName, string ip)
        {
            // Rate limit
            if (!_rateLimitService.RegisterAction(ip, ChatAction.ChangeName))
            {
                Ban(ip, "Name change spam", "ChangeUserName");
                throw new HubException("You are temporarily banned due to spam.");
            }

            var trimmed = TextHelper.SanitizePlainText(newName, 50, allowNewLines: false);
            if (string.IsNullOrEmpty(trimmed))
            {
                _logger.LogDebug("Ignored empty/invalid name change from {ConnectionId} (IP {Ip})", connectionId, ip);
                return;
            }

            string? oldName;
            try
            {
                var changed = _registry.ChangeUserName(connectionId, trimmed, out oldName);
                if (!changed)
                    return; // same name
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "User name already taken: {UserName}", trimmed);
                throw new HubException("This user name is already taken. Please choose another one.");
            }

            // Update history in store
            await _messageStore.UpdateUserNameAsync(connectionId, trimmed);

            _logger.LogInformation("User {ConnectionId} changed name to {UserName} (IP {Ip})",
                connectionId, trimmed, ip);
        }

        public async Task<ChatMessageDto> SendMessageAsync(string connectionId, string text, string ip)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogDebug("Ignored empty message from {ConnectionId}", connectionId);
                throw new HubException("Message is empty.");
            }

            if (!_rateLimitService.RegisterAction(ip, ChatAction.SendMessage))
            {
                Ban(ip, "Message spam", "SendMessage");
                throw new HubException("You are temporarily banned due to spam.");
            }

            if (!_registry.TryGetUserName(connectionId, out var userName) || string.IsNullOrWhiteSpace(userName))
            {
                _logger.LogWarning("Blocked message from {ConnectionId} (IP {Ip}) because user name is not set.",
                    connectionId, ip);
                throw new HubException("User name is not set.");
            }

            var sanitized = TextHelper.SanitizePlainText(text, 500, allowNewLines: true);
            if (string.IsNullOrEmpty(sanitized))
            {
                _logger.LogDebug("Sanitized message is empty from {ConnectionId} (IP {Ip})",
                    connectionId, ip);
                throw new HubException("Message is empty after sanitization.");
            }

            var message = new ChatMessage
            {
                ConnectionId = connectionId,
                UserName = userName!,
                Text = sanitized,
                Timestamp = DateTimeOffset.UtcNow
            };

            await _messageStore.AddAsync(message);

            return new ChatMessageDto
            {
                Id = message.Id,
                ConnectionId = connectionId,
                UserName = message.UserName,
                Text = message.Text,
                Timestamp = message.Timestamp
            };
        }

        public async Task<IReadOnlyList<ChatMessageDto>> GetHistoryAsync(string connectionId, int count, string ip)
        {
            if (!_rateLimitService.RegisterAction(ip, ChatAction.GetHistory))
            {
                Ban(ip, "History spam", "GetHistory");
                throw new HubException("You are temporarily banned due to spam.");
            }

            if (count <= 0) count = 1;
            if (count > 200) count = 200;

            var history = await _messageStore.GetLastAsync(count);

            _logger.LogDebug("Sending history ({Count} messages) to {ConnectionId}",
                history.Count, connectionId);

            return history
                .Select(m => new ChatMessageDto
                {
                    Id = m.Id,
                    ConnectionId = m.ConnectionId,
                    UserName = m.UserName,
                    Text = m.Text,
                    Timestamp = m.Timestamp
                })
                .ToList();
        }

        private void Ban(string ip, string reason, string context)
        {
            var until = _banService.Ban(ip, _banDuration);
            _logger.LogWarning(
                "Spam detected from IP {Ip}. Reason: {Reason}. Banned until {Until}. Context: {Context}.",
                ip, reason, until, context);
        }
    }
}
