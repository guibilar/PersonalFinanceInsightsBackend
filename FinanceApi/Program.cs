using System.Text.Json.Serialization;
using Finance.Application;
using Finance.Application.Reports;
using Finance.Infrastructure;
using Finance.Infrastructure.Events;
using Finance.Infrastructure.Persistence;
using FinanceApi;
using FinanceApi.Auth;
using FinanceApi.Errors;
using FinanceApi.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Import rules ship as their own file; environment variables (Import__...) can still override them.
builder.Configuration
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, Finance.Infrastructure.DependencyInjection.ImportRulesFile), optional: false)
    .AddEnvironmentVariables();

builder.Services.AddControllers()
    // Enums by name only: a number ("status": 7) would otherwise be accepted as a value no enum has, and stored.
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));

builder.Services.AddReverseProxySupport(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<ReportOptions>()
    .Bind(builder.Configuration.GetSection(ReportOptions.SectionName))
    .Validate(options => options.IsValid(), $"{ReportOptions.SectionName}:TimeZone is not a known time zone id.")
    .ValidateOnStart();
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);
builder.Services.AddLogin(builder.Configuration);

// The finance events (T04): an outbox in the finance database, sent to RabbitMQ. Off without Events:RabbitMq.
var events = builder.Configuration.GetSection(FinanceEventsOptions.SectionName).Get<FinanceEventsOptions>() ?? new FinanceEventsOptions();
if (events.Enabled)
{
    builder.Services.AddFinanceEvents(events, Finance.Infrastructure.DependencyInjection.GetConnectionString(builder.Configuration));
}

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: [ReadyTag])
    // T37: Degraded (still 200) while the broker is down; Healthy when the events are off.
    .AddCheck<BrokerHealthCheck>(BrokerHealthCheck.Name, tags: [ReadyTag]);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// In production the frontend calls the API through its own nginx proxy (same origin),
// so CORS is only opened for local development.
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    app.Logger.LogInformation("Applying database migrations");
    await app.Services.MigrateDatabaseAsync();
}
if (events.Enabled)
{
    await app.Services.EnsureFinanceOutboxAsync();
    app.Logger.LogInformation("Finance events are on: outbox in the schema {Schema}, sent to RabbitMQ",
        FinanceEventsOptions.OutboxSchema);
}
if (app.Services.GetRequiredService<IOptions<AuthOptions>>().Value.Mode == AuthMode.Cookie)
{
    await app.Services.EnsureInitialUserAsync(app.Logger);
}
else
{
    app.Logger.LogInformation("Auth mode Gateway: only the gateway tokens of Hestia.Api are accepted (key ids: {KeyIds})",
        string.Join(", ", app.Services.GetRequiredService<FinanceApi.Auth.GatewayAuth.GatewayKeys>().KeyIds));
}
await app.Services.SeedRulesAsync(app.Logger);

// The address of the connection itself, for the Gateway auth mode (tokens only from the internal network).
app.UseGatewayPeer();
// First, so everything after it (the auth cookie in particular) sees the scheme the browser used.
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Finance API V1");
        options.RoutePrefix = string.Empty;
    });
    app.UseCors();
}

app.UseAuthentication();
app.UseAuthorization();
// After authorization, so only requests allowed in count towards an allowance (SignInGuard).
app.UseRateLimiter();

// Liveness: the process answers (no dependencies, so a database outage doesn't get the API restarted).
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
// Readiness: the API can serve requests. /health is kept as an alias for existing monitors.
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) }).AllowAnonymous();
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) }).AllowAnonymous();
app.MapControllers();
app.Run();

// Exposed for WebApplicationFactory in the integration tests.
public partial class Program
{
    private const string ReadyTag = "ready";
}
