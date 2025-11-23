using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Server.Application.Chat;
using Server.Application.Options;
using Server.Application.Security;
using Server.Domain.Security;
using Server.Web.Contracts;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Server.Tests
{
    public class ChatServiceTests
    {
        private static ChatService CreateService(
            out Mock<IMessageStore> messageStore,
            out Mock<IConnectionRegistry> registry,
            out Mock<IBanService> banService,
            out Mock<IRateLimitService> rateLimit,
            SpamProtectionOptions? options = null)
        {
            messageStore = new Mock<IMessageStore>();
            registry = new Mock<IConnectionRegistry>();
            banService = new Mock<IBanService>();
            rateLimit = new Mock<IRateLimitService>();
            var logger = new Mock<ILogger<ChatService>>();

            options ??= new SpamProtectionOptions
            {
                BanDurationSeconds = 10,
                MaxConnectionsPerIp = 5
            };

            var opts = Options.Create(options);

            return new ChatService(
                messageStore.Object,
                registry.Object,
                banService.Object,
                rateLimit.Object,
                opts,
                logger.Object);
        }

        // -----------------------------
        // ChangeUserNameAsync
        // -----------------------------

        [Fact]
        public async Task ChangeUserName_WhenIpIsBanned_ThrowsAndDoesNotCallStoreOrRateLimit()
        {
            var ip = "10.0.0.1";
            var connectionId = "conn-1";

            var service = CreateService(
                out var store,
                out var registry,
                out var ban,
                out var rate);

            TimeSpan? remaining = TimeSpan.FromSeconds(5);
            ban.Setup(b => b.IsBanned(ip, out remaining)).Returns(true);

            await Assert.ThrowsAsync<HubException>(
                () => service.ChangeUserNameAsync(connectionId, "User", ip));

            rate.Verify(r => r.RegisterAction(It.IsAny<string>(), It.IsAny<ChatAction>()), Times.Never);
            store.Verify(s => s.UpdateUserNameAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            ban.Verify(b => b.Ban(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
        }

        [Fact]
        public async Task ChangeUserName_WhenRateLimitExceeded_BansAndThrows()
        {
            var ip = "10.0.0.1";
            var connectionId = "conn-1";

            var options = new SpamProtectionOptions
            {
                BanDurationSeconds = 15,
                MaxConnectionsPerIp = 5
            };

            var service = CreateService(
                out var store,
                out var registry,
                out var ban,
                out var rate,
                options);

            TimeSpan? remaining = null;
            ban.Setup(b => b.IsBanned(ip, out remaining)).Returns(false);

            rate.Setup(r => r.RegisterAction(ip, ChatAction.ChangeName))
                .Returns(false);

            var ex = await Assert.ThrowsAsync<HubException>(
                () => service.ChangeUserNameAsync(connectionId, "User", ip));

            ban.Verify(b => b.Ban(ip, TimeSpan.FromSeconds(options.BanDurationSeconds)), Times.Once);
            store.Verify(s => s.UpdateUserNameAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ChangeUserName_Valid_CallsMessageStore()
        {
            var ip = "10.0.0.1";
            var connectionId = "conn-1";

            var service = CreateService(
                out var store,
                out var registry,
                out var ban,
                out var rate);

            TimeSpan? remaining = null;
            ban.Setup(b => b.IsBanned(ip, out remaining)).Returns(false);
            rate.Setup(r => r.RegisterAction(ip, ChatAction.ChangeName)).Returns(true);

            await service.ChangeUserNameAsync(connectionId, " Michael ", ip);

            store.Verify(s => s.UpdateUserNameAsync(connectionId, It.IsAny<string>()), Times.Once);
        }

        // -----------------------------
        // SendMessageAsync
        // -----------------------------

        [Fact]
        public async Task SendMessage_WhenIpIsBanned_ThrowsAndDoesNotStore()
        {
            var ip = "10.0.0.1";
            var connectionId = "conn-1";

            var service = CreateService(
                out var store,
                out var registry,
                out var ban,
                out var rate);

            TimeSpan? remaining = TimeSpan.FromSeconds(10);
            ban.Setup(b => b.IsBanned(ip, out remaining)).Returns(true);

            await Assert.ThrowsAsync<HubException>(
                () => service.SendMessageAsync(connectionId, "Hello", ip));

            rate.Verify(r => r.RegisterAction(It.IsAny<string>(), It.IsAny<ChatAction>()), Times.Never);
            store.Verify(s => s.AddAsync(It.IsAny<Domain.Chat.ChatMessage>()), Times.Never);
        }

        [Fact]
        public async Task SendMessage_WhenRateLimitExceeded_BansAndThrows()
        {
            var ip = "10.0.0.1";
            var connectionId = "conn-1";

            var options = new SpamProtectionOptions
            {
                BanDurationSeconds = 20,
                MaxConnectionsPerIp = 5
            };

            var service = CreateService(
                out var store,
                out var registry,
                out var ban,
                out var rate,
                options);

            TimeSpan? remaining = null;
            ban.Setup(b => b.IsBanned(ip, out remaining)).Returns(false);
            rate.Setup(r => r.RegisterAction(ip, ChatAction.SendMessage)).Returns(false);

            await Assert.ThrowsAsync<HubException>(
                () => service.SendMessageAsync(connectionId, "spam", ip));

            ban.Verify(b => b.Ban(ip, TimeSpan.FromSeconds(options.BanDurationSeconds)), Times.Once);
            store.Verify(s => s.AddAsync(It.IsAny<Domain.Chat.ChatMessage>()), Times.Never);
        }

        [Fact]
        public async Task SendMessage_Valid_StoresMessage()
        {
            var ip = "10.0.0.1";
            var connectionId = "conn-1";

            var service = CreateService(
                out var store,
                out var registry,
                out var ban,
                out var rate);

            TimeSpan? remaining = null;
            ban.Setup(b => b.IsBanned(ip, out remaining)).Returns(false);
            rate.Setup(r => r.RegisterAction(ip, ChatAction.SendMessage)).Returns(true);

            await service.SendMessageAsync(connectionId, "Hello world", ip);

            store.Verify(s => s.AddAsync(It.IsAny<Domain.Chat.ChatMessage>()), Times.Once);
        }

        // -----------------------------
        // GetHistoryAsync
        // -----------------------------

        [Fact]
        public async Task GetHistory_WhenIpIsBanned_ThrowsAndDoesNotHitStore()
        {
            var ip = "10.0.0.1";
            var connectionId = "conn-1";

            var service = CreateService(
                out var store,
                out var registry,
                out var ban,
                out var rate);

            TimeSpan? remaining = TimeSpan.FromSeconds(5);
            ban.Setup(b => b.IsBanned(ip, out remaining)).Returns(true);

            await Assert.ThrowsAsync<HubException>(
                () => service.GetHistoryAsync(connectionId, 50, ip));

            rate.Verify(r => r.RegisterAction(It.IsAny<string>(), It.IsAny<ChatAction>()), Times.Never);
            store.Verify(s => s.GetLastAsync(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task GetHistory_WhenRateLimitExceeded_BansAndThrows()
        {
            var ip = "10.0.0.1";
            var connectionId = "conn-1";

            var options = new SpamProtectionOptions
            {
                BanDurationSeconds = 30,
                MaxConnectionsPerIp = 5
            };

            var service = CreateService(
                out var store,
                out var registry,
                out var ban,
                out var rate,
                options);

            TimeSpan? remaining = null;
            ban.Setup(b => b.IsBanned(ip, out remaining)).Returns(false);
            rate.Setup(r => r.RegisterAction(ip, ChatAction.GetHistory)).Returns(false);

            await Assert.ThrowsAsync<HubException>(
                () => service.GetHistoryAsync(connectionId, 10, ip));

            ban.Verify(b => b.Ban(ip, TimeSpan.FromSeconds(options.BanDurationSeconds)), Times.Once);
            store.Verify(s => s.GetLastAsync(It.IsAny<int>()), Times.Never);
        }
    }
}
