using Server.Application.Chat;
using Server.Application.Options;
using Server.Application.Security;
using Server.Infrastructure.Chat;
using Server.Infrastructure.Security;
using Server.Web.Hubs;
using Server.Web.Middleware;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // Clear lists to trust any proxy
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// CORS
var allowedOrigins = builder.Configuration
    .GetSection("AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Configs
builder.Services
    .AddOptions<RateLimitOptions>()
    .Bind(builder.Configuration.GetSection("RateLimit"))
    .ValidateOnStart();

builder.Services
    .AddOptions<SpamProtectionOptions>()
    .Bind(builder.Configuration.GetSection("SpamProtection"))
    .ValidateOnStart();

// Services
builder.Services.AddSingleton<IMessageStore, InMemoryMessageStore>();
builder.Services.AddSingleton<IBanService, InMemoryBanService>();
builder.Services.AddSingleton<IRateLimitService, InMemoryRateLimitService>();

builder.Services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddConsole();
});

builder.Services.AddSignalR();

var app = builder.Build();

app.UseForwardedHeaders();

// prod doesn't needs CORS settings, it hosts static files
if (app.Environment.IsDevelopment())
{
    app.UseCors();
}

app.UseMiddleware<IpBanMiddleware>();

// Static files hosting only in prod
if (!app.Environment.IsDevelopment())
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFallbackToFile("/index.html");
}

app.MapHub<ChatHub>("/chathub");

app.Run();


public partial class Program { }
