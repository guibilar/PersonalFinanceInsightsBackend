using System.Text.Json.Serialization;
using Finance.Application;
using Finance.Application.Reports;
using Finance.Infrastructure;
using FinanceApi.Errors;
using FinanceApi.Health;

var builder = WebApplication.CreateBuilder(args);

// Import rules ship as their own file; environment variables (Import__...) can still override them.
builder.Configuration
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, Finance.Infrastructure.DependencyInjection.ImportRulesFile), optional: false)
    .AddEnvironmentVariables();

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

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

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

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

app.MapHealthChecks("/health");
app.MapControllers();
app.Run();

// Exposed for WebApplicationFactory in the integration tests.
public partial class Program;
