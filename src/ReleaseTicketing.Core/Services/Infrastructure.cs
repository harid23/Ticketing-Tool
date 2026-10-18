using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using ReleaseTicketing.Core.Abstractions;
using ReleaseTicketing.Core.Configuration;
using ReleaseTicketing.Core.Data;
using ReleaseTicketing.Core.Domain;
using ReleaseTicketing.Core.Exceptions;

namespace ReleaseTicketing.Core.Services;

/// <summary>Runs the DataAnnotations rules (and IValidatableObject) on a DTO. Used by services as a second line of defence.</summary>
public static class DtoValidator
{
    public static void EnsureValid(object dto)
    {
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true)) return;
        var errors = results.SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : [""]).Select(m => (m, r.ErrorMessage ?? "Invalid")))
            .GroupBy(x => x.m).ToDictionary(g => g.Key, g => g.Select(x => x.Item2).ToArray());
        throw new RequestValidationException(errors);
    }
}

/// <summary>Writes audit rows. It only ADDS to the DbContext; the caller's SaveChanges commits it together with the business change.</summary>
public class AuditService(AppDbContext db, ICurrentUser user, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public void Record(string entityType, object entityId, string action, object? oldValue = null, object? newValue = null, int? releaseRequestId = null)
        => db.AuditLogs.Add(new AuditLog
        {
            EntityType = entityType,
            EntityId = entityId.ToString() ?? "",
            ReleaseRequestId = releaseRequestId,
            Action = action,
            OldValue = oldValue is null ? null : JsonSerializer.Serialize(oldValue, Json),
            NewValue = newValue is null ? null : JsonSerializer.Serialize(newValue, Json),
            PerformedBy = user.UserId,
            PerformedByName = user.DisplayName,
            PerformedAt = clock.GetUtcNow().UtcDateTime,
            IpAddress = user.IpAddress
        });
}

/// <summary>Puts rows into notification_queue. It never sends email - the worker does - so ticket creation can't fail because of email.</summary>
public class NotificationService(AppDbContext db, TimeProvider clock)
{
    public async Task<int> EnqueueAsync(NotificationType type, ReleaseRequest t, IEnumerable<string?> recipients,
        Dictionary<string, string?>? extra = null, string? dedupeScope = null)
    {
        var payload = new Dictionary<string, string?>
        {
            ["TicketNumber"] = t.TicketNumber, ["EmployeeId"] = t.EmployeeId,
            ["EmployeeName"] = t.EmployeeName, ["Status"] = t.Status.ToString().ToUpperInvariant()
        };
        if (extra is not null) foreach (var kv in extra) payload[kv.Key] = kv.Value;
        var json = JsonSerializer.Serialize(payload);
        var now = clock.GetUtcNow().UtcDateTime;
        var added = 0;

        foreach (var r in recipients.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim().ToLowerInvariant()).Distinct())
        {
            var key = dedupeScope is null ? null : $"{type}:{t.TicketNumber}:{dedupeScope}:{r}";
            if (key is not null && await db.NotificationQueue.AnyAsync(n => n.DedupeKey == key)) continue;
            db.NotificationQueue.Add(new NotificationQueueItem
            {
                ReleaseRequest = t, Recipient = r, Type = type, PayloadJson = json,
                Status = NotificationStatus.Pending, CreatedAt = now, NextAttemptAt = now, DedupeKey = key
            });
            added++;
        }
        return added;
    }

    public async Task<List<string>> RoleEmailsAsync(string role)
        => await db.UserRoles.Where(ur => ur.Role.Name == role && ur.User.IsActive).Select(ur => ur.User.Email).ToListAsync();

    public async Task<string?> UserEmailAsync(int? userId)
        => userId is null ? null : await db.Users.Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync();
}

/// <summary>All email wording lives here - not in controllers.</summary>
public static class NotificationTemplates
{
    private static readonly Dictionary<NotificationType, (string Subject, string Body)> T = new()
    {
        [NotificationType.TicketCreated] = ("[{TicketNumber}] Release request created", "Release request {TicketNumber} was created for {EmployeeName} ({EmployeeId})."),
        [NotificationType.ClusterEvaluationRequest] = ("[{TicketNumber}] Action needed: is your cluster interested in {EmployeeName}?", "{EmployeeName} ({EmployeeId}) is being released. Please respond Yes / No before the deadline: {Deadline}."),
        [NotificationType.ClusterInterested] = ("[{TicketNumber}] {Cluster} is interested", "{Cluster} responded YES for {EmployeeName} ({EmployeeId})."),
        [NotificationType.ClusterNotInterested] = ("[{TicketNumber}] {Cluster} is not interested", "{Cluster} responded NO for {EmployeeName} ({EmployeeId})."),
        [NotificationType.AssignmentCreated] = ("[{TicketNumber}] Assigned to {Cluster}", "{EmployeeName} ({EmployeeId}) is now assigned to {Cluster} for evaluation."),
        [NotificationType.InterviewRequired] = ("[{TicketNumber}] Interview required", "Please evaluate/interview {EmployeeName} ({EmployeeId}) and record the outcome."),
        [NotificationType.ResourceSelected] = ("[{TicketNumber}] {EmployeeName} selected by {Cluster}", "{EmployeeName} ({EmployeeId}) was selected by {Cluster}. The ticket is closed."),
        [NotificationType.ResourceNotSelected] = ("[{TicketNumber}] {EmployeeName} not selected by {Cluster}", "{Cluster} did not select {EmployeeName}. Reason: {Reason}"),
        [NotificationType.Reassignment] = ("[{TicketNumber}] Ticket reassigned to {Cluster}", "Ticket {TicketNumber} is now with {Cluster}. {Reason}"),
        [NotificationType.SlaWarning] = ("[{TicketNumber}] SLA reminder", "Ticket {TicketNumber} is approaching its SLA deadline ({Deadline})."),
        [NotificationType.SlaBreach] = ("[{TicketNumber}] SLA BREACHED", "Ticket {TicketNumber} for {EmployeeName} breached its SLA (deadline {Deadline}). Owner: {Owner}."),
        [NotificationType.AutoClosed] = ("[{TicketNumber}] Automatically closed", "Ticket {TicketNumber} was closed automatically: {Reason}."),
        [NotificationType.Cancelled] = ("[{TicketNumber}] Cancelled", "Ticket {TicketNumber} was cancelled. Reason: {Reason}"),
        [NotificationType.Reopened] = ("[{TicketNumber}] Reopened request", "A new request {TicketNumber} was raised from {PreviousTicket} for {EmployeeName}. Reason: {Reason}"),
        [NotificationType.CompetencyNotification] = ("[{TicketNumber}] Competency PMO notification", "Please note the release/rehire information for {EmployeeName} ({EmployeeId}), ticket {TicketNumber}."),
    };

    public static (string Subject, string Body) Render(NotificationType type, IDictionary<string, string?> data)
    {
        var (subject, body) = T[type];
        string Fill(string s) { foreach (var kv in data) s = s.Replace("{" + kv.Key + "}", kv.Value ?? ""); return s; }
        var html = $"<p>{System.Net.WebUtility.HtmlEncode(Fill(body))}</p>" +
                   (data.TryGetValue("AppUrl", out var url) && url is not null ? $"<p><a href=\"{url}\">Open ticket</a></p>" : "");
        return (Fill(subject), html);
    }
}

/// <summary>Working-day maths based on the working_calendar table. Weekends are non-working unless a calendar row says otherwise.</summary>
public class WorkingCalendarService(AppDbContext db)
{
    private async Task<Dictionary<DateOnly, bool>> LoadAsync(DateOnly from, DateOnly to, CancellationToken ct)
        => await db.WorkingCalendar.AsNoTracking().Where(w => w.Date >= from && w.Date <= to).ToDictionaryAsync(w => w.Date, w => w.IsWorkingDay, ct);

    private static bool IsWorking(DateOnly d, Dictionary<DateOnly, bool> overrides)
        => overrides.TryGetValue(d, out var w) ? w : d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    /// <summary>Working days after <paramref name="from"/> up to and including <paramref name="to"/>.</summary>
    public async Task<int> CountWorkingDaysAfterAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to <= from) return 0;
        var o = await LoadAsync(from, to, ct);
        var n = 0;
        for (var d = from.AddDays(1); d <= to; d = d.AddDays(1)) if (IsWorking(d, o)) n++;
        return n;
    }

    public async Task<DateTime> AddWorkingDaysAsync(DateTime from, int days, CancellationToken ct = default)
    {
        var start = DateOnly.FromDateTime(from);
        var o = await LoadAsync(start, start.AddDays(days * 3 + 30), ct);
        var d = start; var left = days;
        while (left > 0) { d = d.AddDays(1); if (IsWorking(d, o)) left--; }
        return d.ToDateTime(TimeOnly.FromDateTime(from), DateTimeKind.Utc);
    }
}

/// <summary>
/// Ticket numbers (RR-2026-000123). One row per year in ticket_counters; the UPDATE ... OUTPUT is atomic,
/// and it runs inside the ticket-creation transaction so a rollback never burns a number.
/// </summary>
public class SqlServerTicketNumberGenerator(AppDbContext db) : ITicketNumberGenerator
{
    public async Task<string> NextAsync(int year, CancellationToken ct)
    {
        if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            var row = await db.TicketCounters.FirstOrDefaultAsync(tc => tc.Year == year, ct);
            if (row is null)
            {
                row = new TicketCounter { Year = year, LastValue = 0 };
                db.TicketCounters.Add(row);
            }

            row.LastValue++;
            await db.SaveChangesAsync(ct);
            return $"RR-{year}-{row.LastValue:D6}";
        }

        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = """
            IF NOT EXISTS (SELECT 1 FROM ticket_counters WITH (UPDLOCK, HOLDLOCK) WHERE [Year] = @y)
                INSERT INTO ticket_counters ([Year], LastValue) VALUES (@y, 0);
            UPDATE ticket_counters SET LastValue = LastValue + 1 OUTPUT INSERTED.LastValue WHERE [Year] = @y;
            """;
        var p = cmd.CreateParameter(); p.ParameterName = "@y"; p.Value = year; cmd.Parameters.Add(p);
        var next = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        return $"RR-{year}-{next:D6}";
    }
}

public class LoggingEmailSender(Microsoft.Extensions.Logging.ILogger<LoggingEmailSender> log) : IEmailSender
{
    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct)
    { log.LogInformation("[DEV EMAIL] To={To} Subject={Subject}", to, subject); return Task.CompletedTask; }
}

public class GraphEmailSender(IOptions<GraphOptions> opts) : IEmailSender
{
    private readonly Microsoft.Graph.GraphServiceClient _client = new(new Azure.Identity.DefaultAzureCredential());

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct)
    {
        var body = new Microsoft.Graph.Users.Item.SendMail.SendMailPostRequestBody
        {
            SaveToSentItems = false,
            Message = new Microsoft.Graph.Models.Message
            {
                Subject = subject,
                Body = new Microsoft.Graph.Models.ItemBody { ContentType = Microsoft.Graph.Models.BodyType.Html, Content = htmlBody },
                ToRecipients = [new Microsoft.Graph.Models.Recipient { EmailAddress = new Microsoft.Graph.Models.EmailAddress { Address = to } }]
            }
        };
        await _client.Users[opts.Value.SenderUser].SendMail.PostAsync(body, cancellationToken: ct);
    }
}

public class AzureBlobStore(IOptions<AttachmentOptions> opts) : IBlobStore
{
    private Azure.Storage.Blobs.BlobContainerClient Container() =>
        new Azure.Storage.Blobs.BlobServiceClient(new Uri(opts.Value.StorageAccountUrl!), new Azure.Identity.DefaultAzureCredential())
            .GetBlobContainerClient(opts.Value.Container);

    public async Task UploadAsync(string path, Stream content, string contentType, CancellationToken ct)
    {
        var c = Container(); await c.CreateIfNotExistsAsync(cancellationToken: ct);
        await c.GetBlobClient(path).UploadAsync(content, new Azure.Storage.Blobs.Models.BlobUploadOptions
        { HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders { ContentType = contentType } }, ct);
    }
    public async Task<Stream> OpenReadAsync(string path, CancellationToken ct) => await Container().GetBlobClient(path).OpenReadAsync(cancellationToken: ct);
}

/// <summary>Development-only store so you can run without an Azure storage account.</summary>
public class LocalDiskBlobStore(IOptions<AttachmentOptions> opts) : IBlobStore
{
    private string Full(string path) => Path.Combine(opts.Value.LocalFolder, path.Replace('/', Path.DirectorySeparatorChar));
    public async Task UploadAsync(string path, Stream content, string contentType, CancellationToken ct)
    {
        var f = Full(path); Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        await using var fs = File.Create(f); await content.CopyToAsync(fs, ct);
    }
    public Task<Stream> OpenReadAsync(string path, CancellationToken ct) => Task.FromResult<Stream>(File.OpenRead(Full(path)));
}
