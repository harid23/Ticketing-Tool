using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ReleaseTicketing.Core.Abstractions;
using ReleaseTicketing.Core.Configuration;
using ReleaseTicketing.Core.Data;

namespace ReleaseTicketing.Core.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>One call that registers everything shared by the Web API and the Azure Functions app.</summary>
    public static IServiceCollection AddReleaseTicketingCore(this IServiceCollection s, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:Default is required. Configure the SQL Server connection string in user-secrets, environment variables, or the deployment secret store.");

        var provider = config["Database:Provider"] ?? "SqlServer";
        if (string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
            s.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));
        else if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
            s.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));
        else
            throw new InvalidOperationException($"Unsupported Database:Provider '{provider}'. Use 'SqlServer' or explicitly select 'Sqlite' for isolated local testing.");

        s.Configure<BusinessOptions>(config.GetSection(BusinessOptions.Section));
        s.Configure<AttachmentOptions>(config.GetSection(AttachmentOptions.Section));
        s.Configure<GraphOptions>(config.GetSection(GraphOptions.Section));
        s.Configure<NotificationOptions>(config.GetSection(NotificationOptions.Section));

        s.AddSingleton(TimeProvider.System);
        s.AddScoped<ITicketNumberGenerator, SqlServerTicketNumberGenerator>();
        s.AddScoped<AuditService>();
        s.AddScoped<NotificationService>();
        s.AddScoped<ActivityFeedService>();
        s.AddScoped<WorkingCalendarService>();
        s.AddScoped<TicketWorkflow>();
        s.AddScoped<ReleaseRequestService>();
        s.AddScoped<EvaluationService>();
        s.AddScoped<AttachmentService>();
        s.AddScoped<ReferenceDataService>();
        s.AddScoped<SlaService>();
        s.AddScoped<NotificationProcessor>();

        if (config.GetValue<bool>("Graph:Enabled")) s.AddSingleton<IEmailSender, GraphEmailSender>();
        else s.AddSingleton<IEmailSender, LoggingEmailSender>();

        if (!string.IsNullOrWhiteSpace(config["Attachments:StorageAccountUrl"])) s.AddSingleton<IBlobStore, AzureBlobStore>();
        else s.AddSingleton<IBlobStore, LocalDiskBlobStore>();
        return s;
    }
}
