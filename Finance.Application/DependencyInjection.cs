using Microsoft.Extensions.DependencyInjection;
using Finance.Application.Reports;
using Finance.Application.Services;
using Finance.Application.Services.Interfaces;

namespace Finance.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ITransactionService, TransactionService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IInsightService, InsightService>();
        services.AddScoped<IBudgetService, BudgetService>();
        services.AddScoped<ICategoryRuleService, CategoryRuleService>();
        services.AddScoped<IFlowRuleService, FlowRuleService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<ITagService, TagService>();
        services.AddSingleton<ReportingCalendar>();
        return services;
    }
}
