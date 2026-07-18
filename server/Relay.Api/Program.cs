using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Relay.Api.Middleware;
using Relay.Api.Security;
using Relay.Infrastructure;
using Relay.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// MVC controllers + RFC 7807 ProblemDetails for error responses. A global
// authorization filter enforces workspace tenancy (404) and role checks (403).
builder.Services.AddControllers(options => options.Filters.Add<WorkspaceAuthorizationFilter>())
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter()));

// JWT bearer authentication. Every endpoint requires an authenticated user by
// default (fallback policy); public endpoints opt out with [AllowAnonymous].
var jwtSettings = new JwtSettings();
builder.Configuration.GetSection(JwtSettings.SectionName).Bind(jwtSettings);
builder.Services.AddSingleton(jwtSettings);
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false; // keep our exact claim type names
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
            NameClaimType = RelayClaims.Name,
            RoleClaimType = RelayClaims.Role,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
    {
        // Make every error response traceable and self-describing.
        context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
        context.ProblemDetails.Extensions["traceId"] =
            System.Diagnostics.Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    });

// EF Core SQLite persistence + scheduling services.
builder.Services.AddInfrastructure(builder.Configuration);

// Rate limiting on trigger endpoints (manual runs + inbound webhooks). The
// permit limit is read from configuration per request (via a policy factory) so
// tests can force a low ceiling with a config override.
const string TriggerPolicy = "triggers";
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(TriggerPolicy, httpContext =>
    {
        var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var permitLimit = config.GetValue("RateLimiting:TriggerPermitLimit", 1000);
        return RateLimitPartition.GetFixedWindowLimiter("global", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(60),
            QueueLimit = 0,
        });
    });
});

// The background scheduler ticks only in the real app; the test host drives the
// ScheduleDispatcher deterministically with a fake clock instead.
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<Relay.Api.Scheduling.ScheduleHostedService>();
}

// Allow the Vite dev/preview client to call the API during development.
const string ClientCors = "client";
builder.Services.AddCors(options =>
{
    options.AddPolicy(ClientCors, policy =>
        policy.WithOrigins(
                "http://localhost:5173",
                "http://localhost:4173")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

// One structured log line per request (method, path, status, elapsed).
app.UseMiddleware<RequestLoggingMiddleware>();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseCors(ClientCors);

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

// Apply migrations and seed on startup (skipped for the test host, which
// configures its own database).
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<RelayDbContext>();
    await db.Database.MigrateAsync();
    await DatabaseSeeder.SeedAsync(db);
}

app.Run();

// Exposed so Relay.Tests can spin the app up with WebApplicationFactory<Program>.
public partial class Program { }
