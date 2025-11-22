using Server.Application.Chat;
using Server.Application.Options;
using Server.Application.Security;
using Server.Infrastructure.Chat;
using Server.Infrastructure.Security;
using Server.Web.Hubs;
using Server.Web.Middleware;

var builder = WebApplication.CreateBuilder(args);

// CORS
// TODO: move AllowedOrigins to be a separate value
// TODO: take care of AllowedHosts value
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
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

app.UseCors();

app.UseMiddleware<IpBanMiddleware>();

app.MapHub<ChatHub>("/chathub");

app.Run();
