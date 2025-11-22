using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Tests.TestInfrastructure
{
    public class CustomWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test");

            builder.ConfigureAppConfiguration((context, config) =>
            {
                var testSettings = new Dictionary<string, string?>
                {
                    // CORS
                    ["AllowedOrigins:0"] = "http://localhost",

                    // RateLimit settings for predictable tests
                    ["RateLimit:Connect:WindowSeconds"] = "5",
                    ["RateLimit:Connect:Limit"] = "5",
                    ["RateLimit:ChangeName:WindowSeconds"] = "5",
                    ["RateLimit:ChangeName:Limit"] = "5",
                    ["RateLimit:SendMessage:WindowSeconds"] = "5",
                    ["RateLimit:SendMessage:Limit"] = "3",   // 3 msgs per 5 seconds
                    ["RateLimit:GetHistory:WindowSeconds"] = "5",
                    ["RateLimit:GetHistory:Limit"] = "3",

                    // SpamProtection
                    ["SpamProtection:BanDurationSeconds"] = "10",
                    ["SpamProtection:MaxConnectionsPerIp"] = "20"
                };

                config.AddInMemoryCollection(testSettings);
            });
        }
    }
}
