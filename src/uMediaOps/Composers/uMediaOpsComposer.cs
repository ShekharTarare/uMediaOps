using uMediaOps.Configuration;
using uMediaOps.Filters;
using uMediaOps.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Migrations;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Infrastructure.Migrations.Upgrade;

namespace uMediaOps.Composers;

/// <summary>
/// Composer for registering Media Ops services and running migrations
/// </summary>
public class uMediaOpsComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        // Register configuration
        builder.Services.AddOptions<uMediaOpsSettings>()
            .BindConfiguration(uMediaOpsSettings.SectionName)
            .ValidateOnStart();

        // Run migrations after Umbraco runtime is ready
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, RunuMediaOpsMigrations>();

        // Register repositories
        builder.Services.AddScoped<Repositories.IFileHashRepository, Repositories.FileHashRepository>();
        builder.Services.AddScoped<Repositories.IReferenceRepository, Repositories.ReferenceRepository>();
        builder.Services.AddScoped<Repositories.IAuditLogRepository, Repositories.AuditLogRepository>();
        builder.Services.AddScoped<Repositories.IAnalyticsRepository, Repositories.AnalyticsRepository>();
        builder.Services.AddScoped<Repositories.IUnusedMediaScanResultRepository, Repositories.UnusedMediaScanResultRepository>();
        builder.Services.AddScoped<Repositories.IBackupRepository, Repositories.BackupRepository>();

        // Register global exception filter
        builder.Services.AddScoped<uMediaOpsExceptionFilter>();
        builder.Services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(options =>
        {
            options.Filters.Add<uMediaOpsExceptionFilter>();
        });

        // Register IMemoryCache (required by CacheService)
        builder.Services.AddMemoryCache();

        // Register services
        builder.Services.AddSingleton<Services.ICacheService, Services.CacheService>();
        builder.Services.AddScoped<Services.IFileHashService, Services.FileHashService>();
        builder.Services.AddScoped<Services.IDuplicateDetectionService, Services.DuplicateDetectionService>();
        builder.Services.AddScoped<Services.IReferenceTrackingService, Services.ReferenceTrackingService>();
        builder.Services.AddScoped<Services.IAuditLogService, Services.AuditLogService>();
        builder.Services.AddScoped<Services.IAnalyticsService, Services.AnalyticsService>();
        builder.Services.AddScoped<Services.IUnusedMediaScanService, Services.UnusedMediaScanService>();
        builder.Services.AddScoped<Services.IBackupService, Services.BackupService>();
    }
}

/// <summary>
/// Notification handler to Run uMediaOps migrations after application has started
/// </summary>
public class RunuMediaOpsMigrations : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private readonly IMigrationPlanExecutor _migrationPlanExecutor;
    private readonly ICoreScopeProvider _scopeProvider;
    private readonly IKeyValueService _keyValueService;

    public RunuMediaOpsMigrations(
        IMigrationPlanExecutor migrationPlanExecutor,
        ICoreScopeProvider scopeProvider,
        IKeyValueService keyValueService)
    {
        _migrationPlanExecutor = migrationPlanExecutor;
        _scopeProvider = scopeProvider;
        _keyValueService = keyValueService;
    }

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var plan = new uMediaOpsMigrationPlan();
            var upgrader = new Upgrader(plan);

            using var scope = _scopeProvider.CreateCoreScope();
            await upgrader.ExecuteAsync(_migrationPlanExecutor, _scopeProvider, _keyValueService);
            scope.Complete();
        }
        catch (Exception)
        {
            // Silently fail if migration cannot run yet
            // This can happen if database is not fully initialized
            // Migration will be retried on next application start
        }
    }
}
