using System.Text.Json.Serialization;
using Finance.Application;
using Finance.Application.Reports;
using Finance.Infrastructure;
using Finance.Infrastructure.Persistence;
using FinanceApi;
using FinanceApi.Auth;
using FinanceApi.Errors;
using FinanceApi.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Import rules ship as their own file; environment variables (Import__...) can still override them.
builder.Configuration
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, Finance.Infrastructure.DependencyInjection.ImportRulesFile), optional: false)
    .AddEnvironmentVariables();

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

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

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: [ReadyTag]);

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
await app.Services.EnsureInitialUserAsync(app.Logger);
await app.Services.SeedCategoryRulesAsync(app.Logger);

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
