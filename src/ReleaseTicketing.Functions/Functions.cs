using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using ReleaseTicketing.Core.Services;

namespace ReleaseTicketing.Functions;

/// <summary>Timer-triggered jobs. Timer triggers are singletons: only one instance runs a given schedule at a time.</summary>
public class ScheduledJobs(SlaService sla, NotificationProcessor notifications, ILogger<ScheduledJobs> log)
{
    // Every 15 minutes: reminders, SLA breach, auto-close. (NCRONTAB: sec min hour day month dow)
    [Function("SlaMonitor")]
    public async Task SlaMonitor([TimerTrigger("0 */15 * * * *")] TimerInfo timer, CancellationToken ct)
    {
        var r = await sla.ProcessAsync(ct);
        log.LogInformation("SlaMonitor finished: {Result}", r);
    }

    // Every minute: send queued emails.
    [Function("NotificationWorker")]
    public async Task NotificationWorker([TimerTrigger("0 * * * * *")] TimerInfo timer, CancellationToken ct)
    {
        var sent = await notifications.ProcessPendingAsync(ct);
        if (sent > 0) log.LogInformation("NotificationWorker sent {Count} emails", sent);
    }
}
