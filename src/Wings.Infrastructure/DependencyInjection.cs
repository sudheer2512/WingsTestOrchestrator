using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wings.Application.Abstractions;
using Wings.Infrastructure.Identity;
using Wings.Infrastructure.Jobs;
using Wings.Infrastructure.Packages;
using Wings.Infrastructure.Persistence;
using Wings.Infrastructure.Security;
using Wings.Infrastructure.Storage;
using Wings.Infrastructure.Tabular;

namespace Wings.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddWingsInfrastructure(this IServiceCollection services)
    {
        // Configuration is read when services resolve (not at registration), so hosts and test factories can override it late.
        static string Provider(IServiceProvider sp) => sp.GetRequiredService<IConfiguration>()["Database:Provider"] ?? "Sqlite";
        static string Connection(IServiceProvider sp) => sp.GetRequiredService<IConfiguration>().GetConnectionString("Wings")
            ?? throw new InvalidOperationException("ConnectionStrings:Wings is not configured.");
        services.AddDbContext<SqlServerWingsDbContext>((sp, o) => o.UseSqlServer(Connection(sp)));
        services.AddDbContext<SqliteWingsDbContext>((sp, o) => o.UseSqlite(Connection(sp)));
        services.AddScoped<WingsDbContext>(sp => Provider(sp).ToLowerInvariant() switch
        {
            "sqlserver" => sp.GetRequiredService<SqlServerWingsDbContext>(),
            "sqlite" => sp.GetRequiredService<SqliteWingsDbContext>(),
            var p => throw new InvalidOperationException($"Unsupported Database:Provider '{p}'. Use Sqlite or SqlServer."),
        });
        services.AddScoped<IWingsDbContext>(sp => sp.GetRequiredService<WingsDbContext>());

        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.Password.RequiredLength = 12;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.User.RequireUniqueEmail = false;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<WingsDbContext>()
            .AddSignInManager();

        services.AddMemoryCache();
        services.AddScoped<ISequenceAllocator, SequenceAllocator>();
        services.AddOptions<StorageOptions>().BindConfiguration("Storage");
        services.AddSingleton<IFileStore, LocalFileStore>();
        services.AddSingleton<JobSignal>();
        services.AddScoped<IJobQueue, JobQueue>();
        services.AddSingleton<JobWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<JobWorker>());
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IProductAccess, ProductAccess>();
        services.AddSingleton<ITabularCodec, CsvCodec>();
        services.AddSingleton<ITabularCodec, XlsxCodec>();
        services.AddOptions<ClamAvOptions>().BindConfiguration("Scanning:ClamAv");
        services.AddSingleton<IContentScanner, BuiltInContentScanner>();
        services.AddSingleton<IContentScanner, ClamAvScanner>();
        services.AddScoped<IJobHandler, PackageValidationJob>();
        services.AddHostedService<ExecutionMonitor>();
        services.AddHostedService<ScheduleRunner>();
        services.AddHostedService<EnvironmentHealthMonitor>();
        services.AddDataProtection().SetApplicationName("Wings.TestOrchestrator");
        services.AddOptions<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>().Configure<Microsoft.Extensions.Options.IOptions<StorageOptions>>((o, storage) =>
            o.XmlRepository = new Microsoft.AspNetCore.DataProtection.Repositories.FileSystemXmlRepository(
                Directory.CreateDirectory(Path.Combine(storage.Value.RootPath, "keys")), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance));
        services.AddSingleton<ISecretProtector, SecretProtector>();
        services.AddSingleton<IPdfRenderer, Reports.PdfRenderer>();
        services.AddScoped<IUserDirectory, Notifications.UserDirectory>();
        services.AddOptions<Notifications.SmtpOptions>().BindConfiguration("Notifications:Smtp");
        services.AddSingleton<Notifications.INotificationSender, Notifications.EmailSender>();
        services.AddSingleton<Notifications.INotificationSender, Notifications.TeamsSender>();
        services.AddSingleton<Notifications.INotificationSender, Notifications.WebhookSender>();
        services.AddScoped<IJobHandler, Notifications.NotificationDeliveryJob>();
        services.AddHostedService<Notifications.QualityMonitor>();
        services.AddOptions<Application.Ai.AiOptions>().BindConfiguration("Ai");
        services.AddSingleton<Application.Ai.IAiModelClient>(sp =>
        {
            var o = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Application.Ai.AiOptions>>().Value;
            return o.Provider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase)
                ? ActivatorUtilities.CreateInstance<Ai.AnthropicModelClient>(sp)
                : new Application.Ai.UnconfiguredAiModelClient($"No AI provider is configured (Ai:Provider is '{o.Provider}').");
        });
        services.AddScoped<DatabaseSeeder>();
        services.TryAddSingleton<IChangeNotifier, NullChangeNotifier>();
        return services;
    }
}
