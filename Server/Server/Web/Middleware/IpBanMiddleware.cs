using Server.Application.Security;
using System.Text.Json;

namespace Server.Web.Middleware
{
    public class IpBanMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<IpBanMiddleware> _logger;

        public IpBanMiddleware(RequestDelegate next, ILogger<IpBanMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, IBanService banService)
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            if (banService.IsBanned(ip, out var remaining))
            {
                _logger.LogWarning(
                    "Blocked HTTP request from banned IP {Ip}. Remaining ban: {Remaining}. Path: {Path}",
                    ip, remaining, context.Request.Path);

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";

                var payload = new
                {
                    error = "banned",
                    message = "You are temporarily blocked due to spam. Please try again later.",
                    retryAfterSeconds = remaining.HasValue
                        ? (int)Math.Ceiling(remaining.Value.TotalSeconds)
                        : (int?)null
                };

                await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
                return;
            }

            await _next(context);
        }
    }

}
