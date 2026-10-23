using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ReleaseTicketing.Core.Abstractions;
using ReleaseTicketing.Core.Configuration;
using ReleaseTicketing.Core.Domain;
using ReleaseTicketing.Core.Services;
using Xunit;

namespace ReleaseTicketing.Tests;

public class NotificationProcessorTests
{
    [Fact]
    public async Task Pending_notification_is_sent_and_recorded_in_history()
    {
        using var h = new Harness();
        var ticket = await h.CreateTicketAsync();
        var notification = await AddNotificationAsync(h, ticket.Id, ticket.TicketNumber);
        var sender = new FakeEmailSender();
        await using var db = h.DbFactory();
        var processor = CreateProcessor(h, db, sender);

        var sent = await processor.ProcessPendingAsync(default);

        Assert.Equal(1, sent);
        var delivered = await db.NotificationQueue.SingleAsync(item => item.NotificationId == notification);
        Assert.Equal(NotificationStatus.Sent, delivered.Status);
        Assert.Equal(h.Clock.GetUtcNow().UtcDateTime, delivered.SentAt);
        var email = Assert.Single(sender.Messages);
        Assert.Equal("recipient@example.test", email.To);
        Assert.Equal($"[{ticket.TicketNumber}] Release request created", email.Subject);
        Assert.Contains(ticket.TicketNumber, email.Body);
        Assert.Contains($"https://example.test/release-requests/{ticket.Id}", email.Body);
        Assert.True(await db.NotificationHistory.AnyAsync(history => history.NotificationId == notification && history.Success));
    }

    [Fact]
    public async Task Failed_notification_retries_after_backoff_then_stops_at_retry_limit()
    {
        using var h = new Harness();
        var ticket = await h.CreateTicketAsync();
        var notification = await AddNotificationAsync(h, ticket.Id, ticket.TicketNumber);
        var sender = new FakeEmailSender { Failure = new InvalidOperationException("mailbox unavailable") };
        var options = new NotificationOptions { MaxRetries = 2, BaseRetryMinutes = 5, AppUrl = "https://example.test/release-requests" };
        await using var db = h.DbFactory();
        var processor = new NotificationProcessor(db, sender, Options.Create(options), h.Clock, NullLogger<NotificationProcessor>.Instance);

        Assert.Equal(0, await processor.ProcessPendingAsync(default));
        var firstAttempt = await db.NotificationQueue.SingleAsync(item => item.NotificationId == notification);
        Assert.Equal(NotificationStatus.Pending, firstAttempt.Status);
        Assert.Equal(1, firstAttempt.RetryCount);
        Assert.Equal(h.Clock.GetUtcNow().UtcDateTime.AddMinutes(5), firstAttempt.NextAttemptAt);
        Assert.Equal(0, await processor.ProcessPendingAsync(default));
        Assert.Equal(1, sender.Attempts);

        h.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(0, await processor.ProcessPendingAsync(default));
        var exhausted = await db.NotificationQueue.SingleAsync(item => item.NotificationId == notification);
        Assert.Equal(NotificationStatus.Failed, exhausted.Status);
        Assert.Equal(2, exhausted.RetryCount);
        Assert.Equal("mailbox unavailable", exhausted.LastError);
        Assert.Equal(2, await db.NotificationHistory.CountAsync(history => history.NotificationId == notification));
        Assert.All(await db.NotificationHistory.Where(history => history.NotificationId == notification).ToListAsync(), history => Assert.False(history.Success));
    }

    private static async Task<int> AddNotificationAsync(Harness harness, int ticketId, string ticketNumber)
    {
        await using var db = harness.DbFactory();
        db.NotificationQueue.RemoveRange(await db.NotificationQueue.ToListAsync());
        await db.SaveChangesAsync();

        var notification = new NotificationQueueItem
        {
            ReleaseRequestId = ticketId,
            Recipient = "recipient@example.test",
            Type = NotificationType.TicketCreated,
            PayloadJson = JsonSerializer.Serialize(new Dictionary<string, string?>
            {
                ["TicketNumber"] = ticketNumber,
                ["EmployeeName"] = "Asha",
                ["EmployeeId"] = "E100"
            }),
            Status = NotificationStatus.Pending,
            CreatedAt = harness.Clock.GetUtcNow().UtcDateTime,
            NextAttemptAt = harness.Clock.GetUtcNow().UtcDateTime
        };
        db.NotificationQueue.Add(notification);
        await db.SaveChangesAsync();
        return notification.NotificationId;
    }

    private static NotificationProcessor CreateProcessor(Harness harness, Core.Data.AppDbContext db, FakeEmailSender sender)
        => new(db, sender, Options.Create(new NotificationOptions { AppUrl = "https://example.test/release-requests" }),
            harness.Clock, NullLogger<NotificationProcessor>.Instance);

    private sealed class FakeEmailSender : IEmailSender
    {
        public List<(string To, string Subject, string Body)> Messages { get; } = [];
        public Exception? Failure { get; init; }
        public int Attempts { get; private set; }

        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct)
        {
            Attempts++;
            if (Failure is not null) throw Failure;
            Messages.Add((to, subject, htmlBody));
            return Task.CompletedTask;
        }
    }
}