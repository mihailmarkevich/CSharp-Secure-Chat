using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Server.Application.Chat;
using Server.Web.Contracts;
using Server.Web.Hubs;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Server.Tests
{
    public class ChatHubTests
    {
        private static (
            ChatHub hub,
            Mock<IChatService> chat,
            Mock<IConnectionRegistry> registry,
            Mock<ILogger<ChatHub>> logger,
            Mock<HubCallerContext> context,
            Mock<IHubCallerClients> clients,
            Mock<IClientProxy> all,
            Mock<ISingleClientProxy> caller
        ) CreateHub(string connectionId = "conn-1", string ip = "10.0.0.1")
        {
            var chat = new Mock<IChatService>();
            var registry = new Mock<IConnectionRegistry>();
            var logger = new Mock<ILogger<ChatHub>>();

            var httpContext = new DefaultHttpContext();
            httpContext.Connection.RemoteIpAddress = IPAddress.Parse(ip);

            var hubContext = new Mock<HubCallerContext>();
            hubContext.SetupGet(c => c.ConnectionId).Returns(connectionId);
            hubContext.Setup(c => c.GetHttpContext()).Returns(httpContext);
            hubContext.Setup(c => c.Abort());

            var allProxy = new Mock<IClientProxy>();
            var callerProxy = new Mock<ISingleClientProxy>();

            var clients = new Mock<IHubCallerClients>();
            clients.SetupGet(c => c.All).Returns(allProxy.Object);
            clients.SetupGet(c => c.Caller).Returns(callerProxy.Object);

            // по умолчанию считаем, что регистрировать соединение можно
            registry
                .Setup(r => r.TryRegisterConnection(
                    connectionId,
                    ip,
                    out It.Ref<int>.IsAny))
                .Returns(true);

            var hub = new ChatHub(chat.Object, registry.Object, logger.Object)
            {
                Context = hubContext.Object,
                Clients = clients.Object
            };

            return (hub, chat, registry, logger, hubContext, clients, allProxy, callerProxy);
        }

        // -----------------------------
        // OnConnectedAsync
        // -----------------------------

        [Fact]
        public async Task OnConnectedAsync_WhenRegistryAllowsConnection_DoesNotBanOrAbort()
        {
            var (hub, _, registry, _, context, _, _, caller) = CreateHub("conn-1", "10.0.0.1");

            int activeConnections = 1;
            registry
                .Setup(r => r.TryRegisterConnection("conn-1", "10.0.0.1", out activeConnections))
                .Returns(true);

            await hub.OnConnectedAsync();

            // никаких Banned
            caller.Verify(
                p => p.SendCoreAsync("Banned", It.IsAny<object[]>(), default),
                Times.Never);

            // никакого Abort
            context.Verify(c => c.Abort(), Times.Never);
        }

        [Fact]
        public async Task OnConnectedAsync_WhenRegistryRejectsConnection_SendsBannedAndAborts()
        {
            var (hub, _, registry, _, context, _, _, caller) = CreateHub("conn-1", "10.0.0.1");

            int activeConnections = 3;
            registry
                .Setup(r => r.TryRegisterConnection("conn-1", "10.0.0.1", out activeConnections))
                .Returns(false);

            object[]? bannedArgs = null;

            caller
                .Setup(p => p.SendCoreAsync("Banned", It.IsAny<object[]>(), default))
                .Callback<string, object[], CancellationToken>((method, args, token) =>
                {
                    bannedArgs = args;
                })
                .Returns(Task.CompletedTask);

            await hub.OnConnectedAsync();

            caller.Verify(
                p => p.SendCoreAsync("Banned", It.IsAny<object[]>(), default),
                Times.Once);

            context.Verify(c => c.Abort(), Times.Once);

            Assert.NotNull(bannedArgs);
            Assert.Single(bannedArgs);
            Assert.IsType<BanNotification>(bannedArgs![0]);
        }

        // -----------------------------
        // OnDisconnectedAsync
        // -----------------------------

        [Fact]
        public async Task OnDisconnectedAsync_UnregistersConnection()
        {
            var (hub, _, registry, _, _, _, _, _) = CreateHub("conn-1", "10.0.0.1");

            await hub.OnDisconnectedAsync(null);

            registry.Verify(r => r.UnregisterConnection("conn-1"), Times.Once);
        }

        // -----------------------------
        // ChangeName
        // -----------------------------

        [Fact]
        public async Task ChangeName_CallsChatService_AndBroadcasts()
        {
            var (hub, chat, _, _, _, _, all, _) = CreateHub("conn-1", "10.0.0.1");

            await hub.ChangeName("Michael");

            chat.Verify(c => c.ChangeUserNameAsync("conn-1", "Michael", "10.0.0.1"), Times.Once);

            all.Verify(p => p.SendCoreAsync(
                    "UserNameChanged",
                    It.Is<object[]>(args =>
                        args.Length == 2 &&
                        (string)args[0] == "conn-1" &&
                        (string)args[1] == "Michael"),
                    default),
                Times.Once);
        }

        [Fact]
        public async Task ChangeName_WhenChatServiceThrows_DoesNotBroadcast()
        {
            var (hub, chat, _, _, _, _, all, _) = CreateHub("conn-1", "10.0.0.1");

            chat.Setup(c => c.ChangeUserNameAsync("conn-1", "Taken", "10.0.0.1"))
                .ThrowsAsync(new HubException("Name already taken"));

            await Assert.ThrowsAsync<HubException>(() => hub.ChangeName("Taken"));

            all.Verify(p => p.SendCoreAsync("UserNameChanged", It.IsAny<object[]>(), default), Times.Never);
        }

        // -----------------------------
        // SendMessage
        // -----------------------------

        [Fact]
        public async Task SendMessage_UsesChatService_AndBroadcastsDto()
        {
            var (hub, chat, _, _, _, _, all, _) = CreateHub("conn-1", "10.0.0.1");

            var dto = new ChatMessageDto
            {
                Id = Guid.NewGuid(),
                ConnectionId = "conn-1",
                UserName = "Michael",
                Text = "Hello",
                Timestamp = DateTimeOffset.UtcNow
            };

            chat.Setup(c => c.SendMessageAsync("conn-1", "Hello", "10.0.0.1"))
                .ReturnsAsync(dto);

            object[]? sentArgs = null;

            all.Setup(p => p.SendCoreAsync(
                    "ReceiveMessage",
                    It.IsAny<object[]>(),
                    default))
               .Callback<string, object[], CancellationToken>((method, args, token) =>
               {
                   sentArgs = args;
               })
               .Returns(Task.CompletedTask);

            await hub.SendMessage("Hello");

            chat.Verify(c => c.SendMessageAsync("conn-1", "Hello", "10.0.0.1"), Times.Once);

            all.Verify(p => p.SendCoreAsync(
                    "ReceiveMessage",
                    It.IsAny<object[]>(),
                    default),
                Times.Once);

            Assert.NotNull(sentArgs);
            Assert.Single(sentArgs);

            var sentDto = Assert.IsType<ChatMessageDto>(sentArgs![0]);
            Assert.Equal("conn-1", sentDto.ConnectionId);
            Assert.Equal("Michael", sentDto.UserName);
            Assert.Equal("Hello", sentDto.Text);
        }

        [Fact]
        public async Task SendMessage_WhenChatServiceThrows_DoesNotBroadcast()
        {
            var (hub, chat, _, _, _, _, all, _) = CreateHub("conn-1", "10.0.0.1");

            chat.Setup(c => c.SendMessageAsync("conn-1", "spam", "10.0.0.1"))
                .ThrowsAsync(new HubException("Banned due to spam"));

            await Assert.ThrowsAsync<HubException>(() => hub.SendMessage("spam"));

            all.Verify(p => p.SendCoreAsync("ReceiveMessage", It.IsAny<object[]>(), default), Times.Never);
        }

        // -----------------------------
        // GetHistory
        // -----------------------------

        [Fact]
        public async Task GetHistory_UsesChatService_AndReturnsResult()
        {
            var (hub, chat, _, _, _, _, _, _) = CreateHub("conn-1", "10.0.0.1");

            var list = new List<ChatMessageDto>
            {
                new ChatMessageDto { Id = Guid.NewGuid() },
                new ChatMessageDto { Id = Guid.NewGuid() }
            };

            chat.Setup(c => c.GetHistoryAsync("conn-1", 50, "10.0.0.1"))
                .ReturnsAsync(list);

            var result = await hub.GetHistory(50);

            chat.Verify(c => c.GetHistoryAsync("conn-1", 50, "10.0.0.1"), Times.Once);
            Assert.Same(list, result);
        }

        [Fact]
        public async Task GetHistory_WhenChatServiceThrows_PropagatesException()
        {
            var (hub, chat, _, _, _, _, _, _) = CreateHub("conn-1", "10.0.0.1");

            chat.Setup(c => c.GetHistoryAsync("conn-1", 10, "10.0.0.1"))
                .ThrowsAsync(new HubException("Banned"));

            await Assert.ThrowsAsync<HubException>(() => hub.GetHistory(10));
        }
    }
}
