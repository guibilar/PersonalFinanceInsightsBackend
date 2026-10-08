using Finance.Application.Reports;
using Finance.Application.Services;
using Finance.Application.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ITransactionService, TransactionService>();
        services.AddScoped<IStatementImportService, StatementImportService>();
        services.AddScoped<TransactionRules>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IInsightService, InsightService>();
        services.AddScoped<IBudgetService, BudgetService>();
        services.AddScoped<ICategoryRuleService, CategoryRuleService>();
        services.AddScoped<IFlowRuleService, FlowRuleService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<ITagService, TagService>();
        services.AddScoped<INetWorthService, NetWorthService>();
        services.AddSingleton<ReportingCalendar>();
        return services;
    }
}
