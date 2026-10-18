using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReleaseTicketing.Core.Abstractions;
using ReleaseTicketing.Core.Configuration;
using ReleaseTicketing.Core.Data;
using ReleaseTicketing.Core.Domain;
using ReleaseTicketing.Core.Dtos;
using ReleaseTicketing.Core.Exceptions;

namespace ReleaseTicketing.Core.Services;

public class AttachmentService(AppDbContext db, IBlobStore blobs, AuditService audit, ICurrentUser user, TimeProvider clock, IOptions<AttachmentOptions> opts)
{
    public async Task<AttachmentDto> UploadAsync(int ticketId, string kind, string fileName, string contentType, long length, Stream content, CancellationToken ct)
    {
        var o = opts.Value;
        var t = await LoadVisibleAsync(ticketId, ct);
        if (!TicketStateMachine.IsActive(t.Status) && !user.IsInRole(RoleNames.Head, RoleNames.Admin))
            throw new BusinessRuleException("Attachments can only be added to active tickets.");
        var uploader = user.UserId ?? throw new ForbiddenException();

        kind = (kind ?? "").ToUpperInvariant();
        if (!AttachmentKinds.All.Contains(kind)) throw new RequestValidationException("kind", $"Kind must be one of: {string.Join(", ", AttachmentKinds.All)}.");
        var safeName = Path.GetFileName(fileName ?? "");
        var ext = Path.GetExtension(safeName).ToLowerInvariant();
        if (string.IsNullOrEmpty(safeName)) throw new RequestValidationException("file", "A file is required.");
        if (!o.AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            throw new RequestValidationException("file", $"File type '{ext}' is not allowed. Allowed: {string.Join(", ", o.AllowedExtensions)}.");
        if (length <= 0) throw new RequestValidationException("file", "The file is empty.");
        if (length > o.MaxFileSizeBytes) throw new RequestValidationException("file", $"File exceeds the {o.MaxFileSizeBytes / (1024 * 1024)} MB limit.");

        var path = $"release-requests/{t.TicketNumber}/{Guid.NewGuid():N}{ext}";   // we choose the name; user input never forms the path
        await blobs.UploadAsync(path, content, contentType, ct);
        var a = new TicketAttachment
        {
            ReleaseRequestId = t.ReleaseRequestId, Kind = kind, FileName = safeName, BlobPath = path,
            FileType = contentType, FileSize = length, UploadedBy = uploader, UploadedAt = clock.GetUtcNow().UtcDateTime
        };
        db.TicketAttachments.Add(a);
        await db.SaveChangesAsync(ct);
        audit.Record("TicketAttachment", a.AttachmentId, "AttachmentUploaded", null, new { safeName, kind, length }, t.ReleaseRequestId);
        await db.SaveChangesAsync(ct);
        return ToDto(a);
    }

    public async Task<IReadOnlyList<AttachmentDto>> ListAsync(int ticketId, CancellationToken ct)
    {
        await LoadVisibleAsync(ticketId, ct);
        var rows = await db.TicketAttachments.AsNoTracking().Where(a => a.ReleaseRequestId == ticketId).OrderBy(a => a.UploadedAt).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    /// <summary>Streams through the API so React never sees a storage URL or key.</summary>
    public async Task<(Stream Content, string FileName, string ContentType)> DownloadAsync(int ticketId, int attachmentId, CancellationToken ct)
    {
        await LoadVisibleAsync(ticketId, ct);
        var a = await db.TicketAttachments.AsNoTracking().FirstOrDefaultAsync(x => x.AttachmentId == attachmentId && x.ReleaseRequestId == ticketId, ct)
            ?? throw new NotFoundException("Attachment not found.");
        audit.Record("TicketAttachment", a.AttachmentId, "AttachmentDownloaded", null, new { a.FileName }, ticketId);
        await db.SaveChangesAsync(ct);
        return (await blobs.OpenReadAsync(a.BlobPath, ct), a.FileName, a.FileType);
    }

    private async Task<ReleaseRequest> LoadVisibleAsync(int ticketId, CancellationToken ct)
    {
        var t = await db.ReleaseRequests.Include(r => r.Evaluations).AsSplitQuery().FirstOrDefaultAsync(r => r.ReleaseRequestId == ticketId, ct)
            ?? throw new NotFoundException($"Release request {ticketId} was not found.");
        var cid = user.ClusterId;
        var canView = user.IsInRole(RoleNames.Admin, RoleNames.Head, RoleNames.Pmo, RoleNames.CompetencyPmo) || t.CreatedBy == user.UserId ||
            (cid != null && (t.InitiatingClusterId == cid || t.CurrentOwnerClusterId == cid || t.Evaluations.Any(e => e.ClusterId == cid)));
        if (!canView) throw new NotFoundException($"Release request {ticketId} was not found.");
        return t;
    }

    private static AttachmentDto ToDto(TicketAttachment a) => new(a.AttachmentId, a.Kind, a.FileName, a.FileType, a.FileSize, a.UploadedAt, a.UploadedBy);
}
