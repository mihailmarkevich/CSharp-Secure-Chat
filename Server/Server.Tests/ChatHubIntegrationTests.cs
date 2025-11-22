using Microsoft.AspNetCore.SignalR.Client;
using Server.Tests.Models;
using Server.Tests.TestInfrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Tests
{
    public class ChatHubIntegrationTests
    {
        private HubConnection CreateHubConnection(CustomWebApplicationFactory factory)
        {
            var baseUri = factory.Server.BaseAddress ?? new Uri("http://localhost");

            return new HubConnectionBuilder()
                .WithUrl(new Uri(baseUri, "/chathub"), options =>
                {
                    options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                })
                .Build();
        }

        [Fact]
        public async Task SendMessage_BroadcastsToAllClients()
        {
            using var factory = new CustomWebApplicationFactory();
            var connection = CreateHubConnection(factory);
            var receivedMessages = new List<MessageDto>();

            connection.On<string, string, string, DateTimeOffset>("ReceiveMessage",
                (id, userName, text, timestamp) =>
                {
                    receivedMessages.Add(new MessageDto
                    {
                        Id = id,
                        UserName = userName,
                        Text = text,
                        Timestamp = timestamp
                    });
                });

            await connection.StartAsync();

            await connection.InvokeAsync("SendMessage", "Hello world");

            await Task.Delay(200);

            await connection.StopAsync();

            Assert.Single(receivedMessages);
            Assert.Equal("Hello world", receivedMessages[0].Text);
            Assert.False(string.IsNullOrWhiteSpace(receivedMessages[0].UserName));
        }

        [Fact]
        public async Task ChangeName_BroadcastsUserNameChanged()
        {
            using var factory = new CustomWebApplicationFactory();
            var connection = CreateHubConnection(factory);
            string? changedConnectionId = null;
            string? newName = null;

            connection.On<string, string>("UserNameChanged", (connectionId, name) =>
            {
                changedConnectionId = connectionId;
                newName = name;
            });

            await connection.StartAsync();

            var targetName = "Michael-Test";
            await connection.InvokeAsync("ChangeName", targetName);

            await Task.Delay(200);

            await connection.StopAsync();

            Assert.NotNull(changedConnectionId);
            Assert.Equal(targetName, newName);
        }

        [Fact]
        public async Task GetHistory_ReturnsLastMessages()
        {
            using var factory = new CustomWebApplicationFactory();
            var connection = CreateHubConnection(factory);
            await connection.StartAsync();

            await connection.InvokeAsync("SendMessage", "msg1");
            await connection.InvokeAsync("SendMessage", "msg2");
            await connection.InvokeAsync("SendMessage", "msg3");

            await Task.Delay(200);

            var history = await connection.InvokeAsync<IReadOnlyList<MessageDto>>("GetHistory", 2);

            await connection.StopAsync();

            Assert.Equal(2, history.Count);
            Assert.Equal("msg2", history[0].Text);
            Assert.Equal("msg3", history[1].Text);
        }

        [Fact]
        public async Task SendMessage_SpamTriggersBan_AndBannedEventIsSent()
        {
            using var factory = new CustomWebApplicationFactory();
            var connection = CreateHubConnection(factory);
            var bannedTcs = new TaskCompletionSource<BanPayload>();

            connection.On<BanPayload>("Banned", payload =>
            {
                bannedTcs.TrySetResult(payload);
            });

            await connection.StartAsync();

            // Within configured rate limit: 3 messages
            await connection.InvokeAsync("SendMessage", "1");
            await connection.InvokeAsync("SendMessage", "2");
            await connection.InvokeAsync("SendMessage", "3");

            // 4th should trigger ban
            try
            {
                await connection.InvokeAsync("SendMessage", "4");
            }
            catch
            {
                // The invocation may fail because the connection is aborted after ban.
                // We don't care about the exception, we care about the Banned event.
            }

            var completed = await Task.WhenAny(bannedTcs.Task, Task.Delay(3000));

            await connection.StopAsync();

            Assert.Same(bannedTcs.Task, completed);
            var ban = bannedTcs.Task.Result;

            Assert.NotNull(ban);
            Assert.False(string.IsNullOrWhiteSpace(ban.Message));
            Assert.True(ban.RetryAfterSeconds.HasValue);
            Assert.True(ban.RetryAfterSeconds.Value > 0);
        }

        [Fact]
        public async Task SendMessage_SpamThenReconnect_IsBlockedByMiddlewareWith403()
        {
            using var factory = new CustomWebApplicationFactory();

            // First connection: trigger ban via spam
            var conn1 = CreateHubConnection(factory);
            var banned1Tcs = new TaskCompletionSource<BanPayload>();

            conn1.On<BanPayload>("Banned", payload =>
            {
                banned1Tcs.TrySetResult(payload);
            });

            await conn1.StartAsync();

            await conn1.InvokeAsync("SendMessage", "1");
            await conn1.InvokeAsync("SendMessage", "2");
            await conn1.InvokeAsync("SendMessage", "3");

            try
            {
                await conn1.InvokeAsync("SendMessage", "4");
            }
            catch
            {
            }

            var completed1 = await Task.WhenAny(banned1Tcs.Task, Task.Delay(3000));
            Assert.Same(banned1Tcs.Task, completed1);

            await conn1.StopAsync();

            // Second connection: IP is banned and IpBanMiddleware returns 403 on negotiate
            var conn2 = CreateHubConnection(factory);

            var ex = await Assert.ThrowsAsync<HttpRequestException>(async () =>
            {
                await conn2.StartAsync();
            });

            // Optional: check that error message mentions 403
            Assert.Contains("403", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

    }
}
