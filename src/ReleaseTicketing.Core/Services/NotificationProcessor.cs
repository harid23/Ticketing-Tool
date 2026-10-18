using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReleaseTicketing.Core.Abstractions;
using ReleaseTicketing.Core.Configuration;
using ReleaseTicketing.Core.Data;
using ReleaseTicketing.Core.Domain;

namespace ReleaseTicketing.Core.Services;

/// <summary>Reads pending rows from notification_queue, sends them, retries with back-off. Run by an Azure Function timer.</summary>
public class NotificationProcessor(AppDbContext db, IEmailSender sender, IOptions<NotificationOptions> opts, TimeProvider clock, ILogger<NotificationProcessor> log)
{
    public async Task<int> ProcessPendingAsync(CancellationToken ct)
    {
        var o = opts.Value; var now = clock.GetUtcNow().UtcDateTime;
        var batch = await db.NotificationQueue.Where(n => n.Status == NotificationStatus.Pending && n.NextAttemptAt <= now)
            .OrderBy(n => n.NextAttemptAt).Take(o.BatchSize).ToListAsync(ct);
        var sent = 0;
        foreach (var n in batch)
        {
            try
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, string?>>(n.PayloadJson) ?? [];
                data["AppUrl"] = $"{o.AppUrl.TrimEnd('/')}/{n.ReleaseRequestId}";
                var (subject, body) = NotificationTemplates.Render(n.Type, data);
                await sender.SendAsync(n.Recipient, subject, body, ct);
                n.Status = NotificationStatus.Sent; n.SentAt = now; sent++;
                db.NotificationHistory.Add(new NotificationHistory { Notification = n, AttemptedAt = now, Success = true });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                n.RetryCount++; n.LastError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                if (n.RetryCount >= o.MaxRetries) n.Status = NotificationStatus.Failed;
                else n.NextAttemptAt = now.AddMinutes(o.BaseRetryMinutes * Math.Pow(2, n.RetryCount - 1));
                db.NotificationHistory.Add(new NotificationHistory { Notification = n, AttemptedAt = now, Success = false, Error = n.LastError });
                log.LogWarning(ex, "Notification {NotificationId} ({Type}) failed, attempt {Attempt}", n.NotificationId, n.Type, n.RetryCount);   // never log body/tokens
            }
        }
        await db.SaveChangesAsync(ct);
        return sent;
    }
}
