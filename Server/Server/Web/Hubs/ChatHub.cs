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

namespace Server.Web.Hubs
{
    public class ChatHub : Hub
    {
        private readonly IMessageStore _messageStore;
        private readonly ILogger<ChatHub> _logger;
        private readonly IBanService _banService;
        private readonly IRateLimitService _rateLimitService;
        private readonly TimeSpan _banDuration;
        private readonly int _maxConnectionsPerIp;
        private const string UnknownIp = "unknown";

        // connectionId -> userName
        private static readonly ConcurrentDictionary<string, string> _userNames = new();

        // connectionId -> ip
        private static readonly ConcurrentDictionary<string, string> _connectionIps = new();

        // ip -> active connection count
        private static readonly ConcurrentDictionary<string, int> _ipConnectionCounts = new();

        public ChatHub(
            IMessageStore messageStore,
            ILogger<ChatHub> logger,
            IBanService banService,
            IRateLimitService rateLimitService, 
            IOptions<SpamProtectionOptions> spamOptions)
        {
            _messageStore = messageStore;
            _logger = logger;
            _banService = banService;
            _rateLimitService = rateLimitService;

            var opts = spamOptions.Value;
            _banDuration = TimeSpan.FromSeconds(opts.BanDurationSeconds);
            _maxConnectionsPerIp = opts.MaxConnectionsPerIp;
        }

        #region Helpers
        private string GetClientIpOrUnknown()
        {
            var httpContext = Context.GetHttpContext();
            var ip = httpContext?.Connection.RemoteIpAddress?.ToString();

            return string.IsNullOrWhiteSpace(ip) ? UnknownIp : ip;
        }

        /// <summary>
        /// Checks whether IP is already banned. 
        /// If yes, sends Banned event to the current connection and aborts it.
        /// Returns true if the action is already handled (ban is active).
        /// </summary>
        private async Task<bool> HandleIfBannedAsync(string ip, string contextInfo)
        {
            if (_banService.IsBanned(ip, out var remainingBan))
            {
                _logger.LogWarning(
                    "Blocked action from banned IP {Ip}, connection {ConnectionId}. Context: {Context}. Remaining ban: {Remaining}.",
                    ip, Context.ConnectionId, contextInfo, remainingBan);

                await Clients.Caller.SendAsync("Banned", new
                {
                    message = "You are temporarily blocked due to spam. Please try again later.",
                    retryAfterSeconds = remainingBan.HasValue
                        ? (int)Math.Ceiling(remainingBan.Value.TotalSeconds)
                        : (int?)null
                });

                // Disconnect the current connection
                Context.Abort();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Bans the IP for configured duration and disconnects the current connection.
        /// </summary>
        private async Task BanAndDisconnectAsync(string ip, string reason, string actionContext)
        {
            var until = _banService.Ban(ip, _banDuration);

            _logger.LogWarning(
                "Spam detected from IP {Ip}. Connection {ConnectionId}. Reason: {Reason}. " +
                "Banned until {Until}. Context: {Context}.",
                ip, Context.ConnectionId, reason, until, actionContext);

            await Clients.Caller.SendAsync("Banned", new
            {
                message = $"You are temporarily blocked for {(int)_banDuration.TotalSeconds} seconds due to spam.",
                retryAfterSeconds = (int)_banDuration.TotalSeconds
            });

            // Disconnect only the current connection
            Context.Abort();
        }
        #endregion

        #region lifecycle
        public override async Task OnConnectedAsync()
        {
            try
            {
                var ip = GetClientIpOrUnknown();

                if (await HandleIfBannedAsync(ip, "OnConnected"))
                    return;

                // Rate limiting for connections
                if (!_rateLimitService.RegisterAction(ip, ChatAction.Connect))
                {
                    await BanAndDisconnectAsync(ip, "Connection spam", "OnConnected");
                    return;
                }

                // Hard limit on concurrent connections per IP
                _ipConnectionCounts.TryGetValue(ip, out var activeConnections);

                if (activeConnections >= _maxConnectionsPerIp)
                {
                    _logger.LogWarning(
                        "IP {Ip} exceeded max concurrent connections limit ({Count} >= {Max}). Aborting connection {ConnectionId}.",
                        ip, activeConnections, _maxConnectionsPerIp, Context.ConnectionId);

                    // optionally also ban this IP
                    //_banService.Ban(ip, _banDuration);

                    await Clients.Caller.SendAsync("Banned", new
                    {
                        message = "Too many active connections from your IP. Please close some sessions and try again.",
                        retryAfterSeconds = (int)_banDuration.TotalSeconds
                    });

                    Context.Abort();
                    return;
                }

                // Register connection
                _ipConnectionCounts.AddOrUpdate(ip, 1, (_, current) => current + 1);
                _connectionIps[Context.ConnectionId] = ip;

                _logger.LogInformation(
                    "Client connected: {ConnectionId} from IP {Ip} (connections from this IP: {Count})",
                    Context.ConnectionId, ip, _ipConnectionCounts[ip]);

                await base.OnConnectedAsync();

            }
            catch (Exception ex)
            {
                // TODO: work on exceptions. Make sure that Front receives usefull messages.
                _logger.LogError(ex,
                    "Unexpected error in OnConnectedAsync for {ConnectionId}",
                    Context.ConnectionId);

                throw;
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            try
            {
                string? ip = null;

                if (_connectionIps.TryRemove(Context.ConnectionId, out var storedIp))
                {
                    ip = storedIp;
                    _ipConnectionCounts.AddOrUpdate(
                        storedIp,
                        _ => 0,
                        (_, current) => Math.Max(0, current - 1));
                }

                if (_userNames.TryRemove(Context.ConnectionId, out var name))
                {
                    _logger.LogInformation(
                        "Client disconnected: {ConnectionId} ({UserName}) from IP {Ip}",
                        Context.ConnectionId, name, ip ?? UnknownIp);
                }
                else
                {
                    _logger.LogWarning(
                        "Client disconnected: {ConnectionId}, but no user entry was found in the dictionary (IP {Ip})",
                        Context.ConnectionId, ip ?? UnknownIp);
                }

                await base.OnDisconnectedAsync(exception);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unexpected error in OnDisconnectedAsync for {ConnectionId}",
                    Context.ConnectionId);

                throw;
            }
        }

        #endregion

        #region public hub methods
        public async Task ChangeName(string newName)
        {
            try
            {
                var ip = GetClientIpOrUnknown();

                if (await HandleIfBannedAsync(ip, "ChangeName"))
                    return;

                if (!_rateLimitService.RegisterAction(ip, ChatAction.ChangeName))
                {
                    await BanAndDisconnectAsync(ip, "Name change spam", "ChangeName");
                    return;
                }

                var trimmed = TextHelper.SanitizePlainText(newName, 50, allowNewLines: false);
                if (string.IsNullOrEmpty(trimmed))
                {
                    _logger.LogDebug(
                        "Ignored empty/invalid name change from {ConnectionId} (IP {Ip})",
                        Context.ConnectionId, ip);
                    return;
                }

                _userNames[Context.ConnectionId] = trimmed;

                _logger.LogInformation(
                    "User {ConnectionId} changed name to {UserName} (IP {Ip})",
                    Context.ConnectionId, trimmed, ip);

                await Clients.All.SendAsync("UserNameChanged", Context.ConnectionId, trimmed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unexpected error in ChangeName for {ConnectionId}",
                    Context.ConnectionId);

                throw;
            }
        }

        public async Task SendMessage(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogDebug(
                        "Ignored empty message from {ConnectionId}",
                        Context.ConnectionId);
                    return;
                }

                var ip = GetClientIpOrUnknown();

                if (await HandleIfBannedAsync(ip, "SendMessage"))
                    return;

                if (!_rateLimitService.RegisterAction(ip, ChatAction.SendMessage))
                {
                    await BanAndDisconnectAsync(ip, "Message spam", "SendMessage");
                    return;
                }

                if (!_userNames.TryGetValue(Context.ConnectionId, out var userName) || string.IsNullOrWhiteSpace(userName))
                {
                    _logger.LogWarning(
                        "Blocked message from {ConnectionId} (IP {Ip}) because user name is not set.",
                        Context.ConnectionId, ip);

                    return;
                }

                text = TextHelper.SanitizePlainText(text, 500, allowNewLines: true);

                if (string.IsNullOrEmpty(text))
                {
                    _logger.LogDebug(
                        "Sanitized message is empty from {ConnectionId} (IP {Ip})",
                        Context.ConnectionId, ip);
                    return;
                }

                var message = new ChatMessage
                {
                    UserName = userName,
                    Text = text,
                    Timestamp = DateTimeOffset.UtcNow
                };

                await _messageStore.AddAsync(message);

                await Clients.All.SendAsync(
                    "ReceiveMessage",
                    message.Id,
                    Context.ConnectionId,
                    message.UserName,
                    message.Text,
                    message.Timestamp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unexpected error in SendMessage for {ConnectionId}",
                    Context.ConnectionId);

                throw;
            }
        }

        public async Task<IReadOnlyList<ChatMessage>> GetHistory(int count = 50)
        {
            try
            {
                var ip = GetClientIpOrUnknown();

                if (await HandleIfBannedAsync(ip, "GetHistory"))
                    return Array.Empty<ChatMessage>();

                if (!_rateLimitService.RegisterAction(ip, ChatAction.GetHistory))
                {
                    await BanAndDisconnectAsync(ip, "History spam", "GetHistory");
                    return Array.Empty<ChatMessage>();
                }

                if (count <= 0)
                    count = 1;

                if (count > 200)
                    count = 200;

                var history = await _messageStore.GetLastAsync(count);

                _logger.LogDebug(
                    "Sending history ({Count} messages) to {ConnectionId}",
                    history.Count, Context.ConnectionId);

                return history;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unexpected error in GetHistory for {ConnectionId}",
                    Context.ConnectionId);

                throw;
            }
        }

        #endregion
    }
}
