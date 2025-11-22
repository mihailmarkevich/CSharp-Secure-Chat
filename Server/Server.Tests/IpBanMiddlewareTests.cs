using Microsoft.Extensions.DependencyInjection;
using Server.Application.Security;
using Server.Tests.TestInfrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Server.Tests
{
    public class IpBanMiddlewareTests
    {
        [Fact]
        public async Task BannedIp_Receives403_WithJsonPayload()
        {
            using var factory = new CustomWebApplicationFactory();

            using (var scope = factory.Services.CreateScope())
            {
                var banService = scope.ServiceProvider.GetRequiredService<IBanService>();

                // For TestServer, RemoteIpAddress is often null, which falls back to "unknown" in middleware.
                banService.Ban("unknown", TimeSpan.FromSeconds(30));
            }

            var client = factory.CreateClient();

            var response = await client.GetAsync("/test");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            var root = doc.RootElement;
            Assert.Equal("banned", root.GetProperty("error").GetString());
            Assert.NotNull(root.GetProperty("message").GetString());
            var retryAfter = root.GetProperty("retryAfterSeconds");
            Assert.True(retryAfter.ValueKind == JsonValueKind.Number || retryAfter.ValueKind == JsonValueKind.Null);
        }
    }
}
