using System.Text.Json;
using System.Text.Json.Serialization;
using CrossCutting.Services;
using CrossCutting.Services.Interfaces;
using FinanceDomain;
using FinanceDomain.Repositories;
using FinanceDomain.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddDbContext<FinanceDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"), npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null
        );
    }));


builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
builder.Services.AddScoped<IBudgetRepository, BudgetRepository>();

builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IBudgetService, BudgetService>();


builder.Services.AddEndpointsApiExplorer();

// Validate database connection on startup
using (var scope = builder.Services.BuildServiceProvider().CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
    try
    {
        Console.WriteLine("Verifying database connectivity...");
        dbContext.Database.CanConnect();
        Console.WriteLine("Database connectivity verified successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Database connectivity test failed: {ex.Message}");
        throw;
    }
}
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});


var app = builder.Build();
app.UseRouting();
Console.WriteLine("Starting application...");

// Enable Swagger in all environments
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Finance API V1");
    c.RoutePrefix = string.Empty; // Serve Swagger at the root
});

Console.WriteLine("Swagger is configured. Visit /swagger/index.html to access the API documentation.");

app.UseCors();

app.UseAuthorization();

app.MapControllers();
app.Run();