using Microsoft.Extensions.DependencyInjection;
using Wings.Application.Abstractions;
using Wings.Application.Administration;
using Wings.Application.Features;
using Wings.Application.Testing;

namespace Wings.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddWingsApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IFeatureQueryService, FeatureQueryService>();
        services.AddScoped<IFeatureService, FeatureService>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddScoped<IMasterDataService, MasterDataService>();
        services.AddScoped<TestCaseService>();
        services.AddScoped<ITestCaseService>(sp => sp.GetRequiredService<TestCaseService>());
        services.AddScoped<ITestSuiteService, TestSuiteService>();
        services.AddScoped<ITestFolderService, TestFolderService>();
        services.AddScoped<IPackageService, PackageService>();
        services.AddOptions<PackageOptions>().BindConfiguration("Packages");
        services.AddOptions<Execution.ExecutionOptions>().BindConfiguration("Execution");
        services.AddScoped<Execution.ExecutionEngine>();
        services.AddScoped<Execution.IExecutionService, Execution.ExecutionService>();
        services.AddScoped<Execution.IAgentGateway, Execution.AgentGateway>();
        services.AddScoped<Execution.IAgentAdminService, Execution.AgentAdminService>();
        services.AddHttpClient();
        services.AddScoped<Orchestration.IEnvironmentService, Orchestration.EnvironmentService>();
        services.AddScoped<Orchestration.ITestDataService, Orchestration.TestDataService>();
        services.AddScoped<Orchestration.IPlanService, Orchestration.PlanService>();
        services.AddScoped<Orchestration.ICampaignService, Orchestration.CampaignService>();
        services.AddScoped<Orchestration.IScheduleService, Orchestration.ScheduleService>();

        services.AddOptions<Quality.NotificationOptions>().BindConfiguration("Notifications");
        services.AddHttpClient(Quality.IssueTrackerService.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddScoped<Quality.INotificationPublisher, Quality.NotificationPublisher>();
        services.AddScoped<Quality.INotificationService, Quality.NotificationService>();
        services.AddScoped<Quality.IDefectService, Quality.DefectService>();
        services.AddSingleton<Quality.IIssueTrackerAdapter, Quality.GitHubAdapter>();
        services.AddSingleton<Quality.IIssueTrackerAdapter, Quality.JiraAdapter>();
        services.AddSingleton<Quality.IIssueTrackerAdapter, Quality.AzureDevOpsAdapter>();
        services.AddSingleton<Quality.IIssueTrackerAdapter, Quality.WebhookTrackerAdapter>();
        services.AddScoped<Quality.IIssueTrackerService, Quality.IssueTrackerService>();
        services.AddScoped<Quality.IReleaseService, Quality.ReleaseService>();
        services.AddSingleton<Quality.ISourceAdapter, Quality.GitSourceAdapter>();
        services.AddSingleton<Quality.ISourceAdapter, Quality.TfvcSourceAdapter>();
        services.AddScoped<Quality.IBuildService, Quality.BuildService>();
        services.AddScoped<Quality.IDashboardService, Quality.DashboardService>();
        services.AddScoped<Quality.IReportService, Quality.ReportService>();
        services.AddScoped<IJobHandler, Quality.RunFinishedJob>();
        services.AddScoped<Quality.OperationsWatch>();
        services.AddScoped<Ai.IAiAssistant, Ai.AiAssistant>();
        return services;
    }
}
