using Microsoft.Extensions.Options;
using Server.Application.Chat;
using Server.Application.Options;
using Server.Application.Security;
using System;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Server.Domain.Security;
using Server.Helpers;
using Server.Domain.Chat;
using Server.Web.Contracts;

namespace Server.Web.Hubs
{
    public class ChatHub : Hub
    {
        private readonly IChatService _chatService;
        private readonly IConnectionRegistry _registry;
        private readonly ILogger<ChatHub> _logger;
        private const string UnknownIp = "unknown";

        public ChatHub(
            IChatService chatService,
            IConnectionRegistry registry,
            ILogger<ChatHub> logger)
        {
            _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private string GetClientIpOrUnknown()
        {
            var httpContext = Context.GetHttpContext();
            var ip = httpContext?.Connection.RemoteIpAddress?.ToString();
            return string.IsNullOrWhiteSpace(ip) ? UnknownIp : ip;
        }

        #region lifecycle

        public override async Task OnConnectedAsync()
        {
            var ip = GetClientIpOrUnknown();
            var connectionId = Context.ConnectionId;

            if (!_registry.TryRegisterConnection(connectionId, ip, out var activeConnections))
            {
                _logger.LogWarning(
                    "IP {Ip} exceeded max concurrent connections limit. Aborting connection {ConnectionId}.",
                    ip, connectionId);

                var payload = new BanNotification
                {
                    Message = "Too many active connections from your IP. Please close some sessions and try again.",
                    RetryAfterSeconds = null
                };

                await Clients.Caller.SendAsync("Banned", payload);
                Context.Abort();
                return;
            }

            _logger.LogInformation(
                "Client connected: {ConnectionId} from IP {Ip} (connections from this IP: {Count})",
                connectionId, ip, activeConnections);

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var connectionId = Context.ConnectionId;

            _registry.UnregisterConnection(connectionId);

            _logger.LogInformation(
                "Client disconnected: {ConnectionId}",
                connectionId);

            await base.OnDisconnectedAsync(exception);
        }

        #endregion

        #region public hub methods

        public async Task ChangeName(string newName)
        {
            var ip = GetClientIpOrUnknown();
            var connectionId = Context.ConnectionId;

            await _chatService.ChangeUserNameAsync(connectionId, newName, ip);

            await Clients.All.SendAsync("UserNameChanged", connectionId, newName);
        }

        public async Task SendMessage(string text)
        {
            var ip = GetClientIpOrUnknown();
            var connectionId = Context.ConnectionId;

            var dto = await _chatService.SendMessageAsync(connectionId, text, ip);

            await Clients.All.SendAsync("ReceiveMessage", dto);
        }

        public async Task<IReadOnlyList<ChatMessageDto>> GetHistory(int count)
        {
            var ip = GetClientIpOrUnknown();
            var connectionId = Context.ConnectionId;

            var history = await _chatService.GetHistoryAsync(connectionId, count, ip);
            return history;
        }

        #endregion
    }
}
