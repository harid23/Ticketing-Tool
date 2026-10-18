using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReleaseTicketing.Core.Abstractions;
using ReleaseTicketing.Core.Configuration;
using ReleaseTicketing.Core.Data;
using ReleaseTicketing.Core.Domain;
using ReleaseTicketing.Core.Dtos;
using ReleaseTicketing.Core.Exceptions;

namespace ReleaseTicketing.Core.Services;

public class ReleaseRequestService(
    AppDbContext db, AuditService audit, NotificationService notify, TicketWorkflow wf, WorkingCalendarService calendar,
    ITicketNumberGenerator numbers, ICurrentUser user, TimeProvider clock, IOptions<BusinessOptions> biz)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ---------- Create ----------
    public async Task<ReleaseRequestDetailDto> CreateAsync(CreateReleaseRequestDto dto, CancellationToken ct)
    {
        user.Require(RoleNames.Pm, RoleNames.Sdm, RoleNames.Pmo, RoleNames.Admin);
        DtoValidator.EnsureValid(dto);
        var creatorId = user.UserId ?? throw new ForbiddenException();

        var cluster = await db.Clusters.FirstOrDefaultAsync(c => c.Id == dto.ClusterId && c.IsActive, ct)
            ?? throw new RequestValidationException("clusterId", "Unknown or inactive cluster.");
        var category = await db.ReleaseCategories.FirstOrDefaultAsync(c => c.Id == dto.ReleaseCategoryId && c.IsActive, ct)
            ?? throw new RequestValidationException("releaseCategoryId", "Unknown release category.");
        var sla = await GetSlaAsync(ct);

        var empId = dto.EmployeeId.Trim();
        var master = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.EmployeeId == empId && e.IsActive, ct);
        var overrides = master is null ? null : MasterDifferences(master, dto);
        var source = master is not null && overrides!.Count == 0 ? EmployeeSource.Master : EmployeeSource.Manual;

        var now = Now;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var t = new ReleaseRequest
        {
            TicketNumber = await numbers.NextAsync(now.Year, ct),
            EmployeeId = empId, EmployeeName = dto.EmployeeName.Trim(), EmployeeSource = source,
            ReleaseDate = dto.ReleaseDate!.Value, ReasonForRelease = dto.ReasonForRelease.Trim(), ReleaseCategoryId = category.Id,
            RecommendedForNewAssignment = dto.RecommendedForNewAssignment, EmployeeAware = dto.EmployeeAware,
            EmployeeSkills = dto.EmployeeSkills.Trim(), FeedbackProvided = dto.FeedbackProvided.Trim(),
            DetailedFeedback = dto.DetailedFeedback.Trim(), CommitmentMade = dto.CommitmentMade?.Trim(), FactoredInForecast = dto.FactoredInForecast,
            Grade = dto.Grade.Trim(), Location = dto.Location.Trim(), CurrentSupervisor = dto.CurrentSupervisor.Trim(),
            CurrentProjectId = dto.CurrentProjectId.Trim(), CurrentProjectName = dto.CurrentProjectName.Trim(),
            Remarks = dto.Remarks?.Trim(), ReleaseLocation = dto.ReleaseLocation.Trim(), Psl = dto.Psl.Trim(), Band = dto.Band.Trim(),
            BackfillRequired = dto.BackfillRequired, InitiatingClusterId = cluster.Id,
            Status = TicketStatus.Open, SlaStatus = SlaStatus.WithinSla,
            CreatedBy = creatorId, CreatedByName = user.DisplayName, CreatedAt = now, UpdatedAt = now
        };
        t.ReleaseCategory = category;

        t.EvaluationStartAt = now;
        t.EvaluationDeadlineAt = sla.CalendarType == "WORKING"
            ? await calendar.AddWorkingDaysAsync(now, sla.DurationDays, ct)
            : now.AddDays(sla.DurationDays);

        if (category.RequiresClusterCirculation)
        {
            var targets = await db.Clusters.Where(c => c.IsActive && c.Id != cluster.Id).ToListAsync(ct);
            foreach (var c in targets)
                t.Evaluations.Add(new ClusterEvaluation { Cluster = c, Status = EvaluationStatus.Pending });
        }

        db.ReleaseRequests.Add(t);
        await db.SaveChangesAsync(ct);   // first save: gives us ReleaseRequestId so history/audit can reference it

        db.TicketStatusHistory.Add(new TicketStatusHistory
        {
            ReleaseRequestId = t.ReleaseRequestId, EventType = "StatusChange", OldStatus = null, NewStatus = TicketStatus.Open,
            ChangedBy = creatorId, ChangedByName = user.DisplayName, ChangedAt = now, Reason = "Ticket created"
        });
        audit.Record("ReleaseRequest", t.ReleaseRequestId, "Created", null, new { t.TicketNumber, t.EmployeeId, Category = category.Code, Source = source }, t.ReleaseRequestId);
        if (master is null) audit.Record("ReleaseRequest", t.ReleaseRequestId, "EmployeeManualEntry", null, new { t.EmployeeId, t.EmployeeName }, t.ReleaseRequestId);
        else if (overrides!.Count > 0) audit.Record("ReleaseRequest", t.ReleaseRequestId, "EmployeeMasterOverride", null, overrides, t.ReleaseRequestId);

        if (!category.RequiresClusterCirculation)
        {
            t.AssignedToHead = true;
            wf.ChangeStatus(t, TicketStatus.Wip, $"Direct release flow ({category.Code}): no cluster circulation");
            wf.AddEvent(t, "Assignment", "Assigned to Head for direct release/offboarding");
        }

        var head = await notify.RoleEmailsAsync(RoleNames.Head);
        await notify.EnqueueAsync(NotificationType.TicketCreated, t, head.Append(await notify.UserEmailAsync(creatorId)));
        foreach (var e in t.Evaluations)
            await notify.EnqueueAsync(NotificationType.ClusterEvaluationRequest, t, [e.Cluster.NotificationEmail, await notify.UserEmailAsync(e.Cluster.ManagerUserId)],
                new() { ["Cluster"] = e.Cluster.Code, ["Deadline"] = t.EvaluationDeadlineAt?.ToString("yyyy-MM-dd") });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);   // ticket, evaluations, history, audit and notification rows all commit together
        return await GetAsync(t.ReleaseRequestId, ct);
    }

    private static Dictionary<string, object> MasterDifferences(Employee m, CreateReleaseRequestDto d)
    {
        var diffs = new Dictionary<string, object>();
        void Check(string field, string? master, string? submitted)
        {
            if (!string.Equals((master ?? "").Trim(), (submitted ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                diffs[field] = new { master, submitted };
        }
        Check("employeeName", m.Name, d.EmployeeName); Check("grade", m.Grade, d.Grade); Check("location", m.Location, d.Location);
        Check("supervisor", m.Supervisor, d.CurrentSupervisor); Check("projectId", m.ProjectId, d.CurrentProjectId);
        Check("projectName", m.ProjectName, d.CurrentProjectName); Check("psl", m.Psl, d.Psl); Check("band", m.Band, d.Band);
        return diffs;
    }

    // ---------- Read ----------
    public async Task<ReleaseRequestDetailDto> GetAsync(int id, CancellationToken ct)
    {
        var t = await Visible(db.ReleaseRequests.AsNoTracking())
            .Include(x => x.ReleaseCategory).Include(x => x.InitiatingCluster).Include(x => x.CurrentOwnerCluster)
            .Include(x => x.CurrentOwnerUser).Include(x => x.ReopenedFrom)
            .Include(x => x.Evaluations).ThenInclude(e => e.Cluster).AsSplitQuery()
            .FirstOrDefaultAsync(x => x.ReleaseRequestId == id, ct)
            ?? throw new NotFoundException($"Release request {id} was not found.");   // 404 (not 403) so existence isn't leaked
        var exceptionApproved = await db.AuditLogs.AsNoTracking().AnyAsync(
            audit => audit.ReleaseRequestId == id && audit.Action == "SlaExceptionApproved", ct);
        return ToDetail(t, exceptionApproved);
    }

    public async Task<PagedResult<ReleaseRequestSummaryDto>> ListAsync(ReleaseRequestQuery q, CancellationToken ct)
    {
        var query = Visible(db.ReleaseRequests.AsNoTracking());
        if (!string.IsNullOrWhiteSpace(q.TicketNumber)) query = query.Where(t => t.TicketNumber.Contains(q.TicketNumber.Trim()));
        if (!string.IsNullOrWhiteSpace(q.EmployeeId)) query = query.Where(t => t.EmployeeId.Contains(q.EmployeeId.Trim()));
        if (!string.IsNullOrWhiteSpace(q.EmployeeName)) query = query.Where(t => t.EmployeeName.Contains(q.EmployeeName.Trim()));
        if (q.ClusterId is { } c) query = query.Where(t => t.InitiatingClusterId == c || t.CurrentOwnerClusterId == c || t.Evaluations.Any(e => e.ClusterId == c));
        if (ParseEnum<TicketStatus>(q.Status) is { } st) query = query.Where(t => t.Status == st);
        if (ParseEnum<SlaStatus>(q.SlaStatus) is { } sl) query = query.Where(t => t.SlaStatus == sl);
        if (q.OwnerUserId is { } o) query = query.Where(t => t.CurrentOwnerUserId == o);
        if (q.ReleaseCategoryId is { } rc) query = query.Where(t => t.ReleaseCategoryId == rc);
        if (q.ReleaseDateFrom is { } rf) query = query.Where(t => t.ReleaseDate >= rf);
        if (q.ReleaseDateTo is { } rt) query = query.Where(t => t.ReleaseDate <= rt);
        if (q.CreatedFrom is { } cf) query = query.Where(t => t.CreatedAt >= cf);
        if (q.CreatedTo is { } ct2) query = query.Where(t => t.CreatedAt <= ct2);
        if (q.Mine == true) { var me = user.UserId; query = query.Where(t => t.CreatedBy == me); }

        var page = Math.Max(1, q.Page); var size = Math.Clamp(q.PageSize, 1, 100);
        Expression<Func<ReleaseRequest, object>> sort = q.SortBy?.ToLowerInvariant() switch
        {
            "ticketnumber" => t => t.TicketNumber, "employeename" => t => t.EmployeeName, "status" => t => t.Status,
            "releasedate" => t => t.ReleaseDate, "slastatus" => t => t.SlaStatus, "deadline" => t => t.EvaluationDeadlineAt!,
            _ => t => t.CreatedAt
        };
        var desc = !string.Equals(q.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);
        var ordered = (desc ? query.OrderByDescending(sort) : query.OrderBy(sort)).ThenBy(t => t.ReleaseRequestId);

        var total = await query.CountAsync(ct);
        var items = await ordered.Skip((page - 1) * size).Take(size)
            .Select(t => new ReleaseRequestSummaryDto(t.ReleaseRequestId, t.TicketNumber, t.EmployeeId, t.EmployeeName,
                t.InitiatingCluster.Code, t.ReleaseCategory.Name, t.Status, t.SlaStatus,
                t.CurrentOwnerCluster == null ? null : t.CurrentOwnerCluster.Code, t.AssignedToHead,
                t.ReleaseDate, t.CreatedAt, t.EvaluationDeadlineAt))
            .ToListAsync(ct);
        return new PagedResult<ReleaseRequestSummaryDto>(items, page, size, total);
    }

    public async Task<IReadOnlyList<HistoryDto>> GetHistoryAsync(int id, CancellationToken ct)
    {
        await EnsureVisibleAsync(id, ct);
        return await db.TicketStatusHistory.AsNoTracking().Where(h => h.ReleaseRequestId == id).OrderBy(h => h.ChangedAt).ThenBy(h => h.HistoryId)
            .Select(h => new HistoryDto(h.HistoryId, h.EventType, h.OldStatus, h.NewStatus, h.ChangedByName, h.ChangedAt, h.Reason, h.Comments)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AuditDto>> GetAuditAsync(int id, CancellationToken ct)
    {
        user.Require(RoleNames.Head, RoleNames.Admin, RoleNames.Pmo);
        await EnsureVisibleAsync(id, ct);
        return await db.AuditLogs.AsNoTracking().Where(a => a.ReleaseRequestId == id).OrderBy(a => a.PerformedAt).ThenBy(a => a.AuditId)
            .Select(a => new AuditDto(a.AuditId, a.EntityType, a.EntityId, a.Action, a.OldValue, a.NewValue, a.PerformedByName, a.PerformedAt, a.IpAddress)).ToListAsync(ct);
    }

    // ---------- Update ----------
    public async Task<ReleaseRequestDetailDto> UpdateAsync(int id, UpdateReleaseRequestDto dto, CancellationToken ct)
    {
        DtoValidator.EnsureValid(dto);
        var t = await wf.LoadAsync(id, ct);
        if (!CanEdit(t)) throw new ForbiddenException();
        if (!TicketStateMachine.IsActive(t.Status)) throw new BusinessRuleException("Closed or cancelled tickets cannot be edited.");

        var oldV = new Dictionary<string, object?>(); var newV = new Dictionary<string, object?>();
        void Set<T>(string name, T current, T incoming, Action<T> apply)
        {
            if (EqualityComparer<T>.Default.Equals(current, incoming)) return;
            oldV[name] = current; newV[name] = incoming; apply(incoming);
        }
        Set("releaseDate", t.ReleaseDate, dto.ReleaseDate!.Value, v => t.ReleaseDate = v);
        Set("reasonForRelease", t.ReasonForRelease, dto.ReasonForRelease.Trim(), v => t.ReasonForRelease = v);
        Set("recommendedForNewAssignment", t.RecommendedForNewAssignment, dto.RecommendedForNewAssignment, v => t.RecommendedForNewAssignment = v);
        Set("employeeAware", t.EmployeeAware, dto.EmployeeAware, v => t.EmployeeAware = v);
        Set("employeeSkills", t.EmployeeSkills, dto.EmployeeSkills.Trim(), v => t.EmployeeSkills = v);
        Set("feedbackProvided", t.FeedbackProvided, dto.FeedbackProvided.Trim(), v => t.FeedbackProvided = v);
        Set("detailedFeedback", t.DetailedFeedback, dto.DetailedFeedback.Trim(), v => t.DetailedFeedback = v);
        Set("commitmentMade", t.CommitmentMade, dto.CommitmentMade?.Trim(), v => t.CommitmentMade = v);
        Set("factoredInForecast", t.FactoredInForecast, dto.FactoredInForecast, v => t.FactoredInForecast = v);
        Set("remarks", t.Remarks, dto.Remarks?.Trim(), v => t.Remarks = v);
        Set("releaseLocation", t.ReleaseLocation, dto.ReleaseLocation.Trim(), v => t.ReleaseLocation = v);
        Set("backfillRequired", t.BackfillRequired, dto.BackfillRequired, v => t.BackfillRequired = v);

        if (newV.Count > 0)
        {
            t.UpdatedAt = Now;
            audit.Record("ReleaseRequest", id, "FieldsModified", oldV, newV, id);
            wf.AddEvent(t, "Edited", $"Fields modified: {string.Join(", ", newV.Keys)}");
            await db.SaveChangesAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    // ---------- Cancel ----------
    public async Task<ReleaseRequestDetailDto> CancelAsync(int id, CancelDto dto, CancellationToken ct)
    {
        DtoValidator.EnsureValid(dto);
        var t = await wf.LoadAsync(id, ct);
        if (!CanCancelRole(t)) throw new ForbiddenException();
        if (!TicketStateMachine.IsActive(t.Status)) throw new BusinessRuleException($"A {t.Status} ticket cannot be cancelled.");

        var sla = await GetSlaAsync(ct);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(biz.Value.BusinessTimeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(Now, tz));
        var identified = dto.OpportunityIdentifiedOn!.Value;
        if (identified > today) throw new RequestValidationException("opportunityIdentifiedOn", "The identification date cannot be in the future.");

        var used = await calendar.CountWorkingDaysAfterAsync(identified, today, ct);
        if (used > sla.CancellationWindowWorkingDays)
            throw new BusinessRuleException($"Cancellation is only allowed within {sla.CancellationWindowWorkingDays} working days of identifying the opportunity; {used} have passed.");

        t.CancellationReason = dto.Reason.Trim(); t.CancelledAt = Now;
        wf.ChangeStatus(t, TicketStatus.Cancelled, "Cancelled", dto.Reason.Trim());
        audit.Record("ReleaseRequest", id, "Cancelled", null, new { dto.Reason, identified, workingDaysUsed = used }, id);

        var interested = t.Evaluations.Where(e => e.Status is EvaluationStatus.Interested or EvaluationStatus.Assigned or EvaluationStatus.InProgress).ToList();
        foreach (var e in interested)
            await notify.EnqueueAsync(NotificationType.Cancelled, t, [e.Cluster.NotificationEmail, await notify.UserEmailAsync(e.Cluster.ManagerUserId)], new() { ["Reason"] = dto.Reason });
        await notify.EnqueueAsync(NotificationType.Cancelled, t, await notify.RoleEmailsAsync(RoleNames.Head), new() { ["Reason"] = dto.Reason });

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // ---------- Close (Head/Admin) ----------
    public async Task<ReleaseRequestDetailDto> CloseAsync(int id, CloseDto dto, CancellationToken ct)
    {
        user.Require(RoleNames.Asdm, RoleNames.Admin);
        DtoValidator.EnsureValid(dto);
        var t = await wf.LoadAsync(id, ct);
        if (!TicketStateMachine.IsActive(t.Status))
            throw new BusinessRuleException("This request is already closed.");
        if (user.IsInRole(RoleNames.Asdm) && !user.IsInRole(RoleNames.Admin) &&
            (user.ClusterId is null || (user.ClusterId != t.InitiatingClusterId &&
                !t.Evaluations.Any(evaluation => evaluation.ClusterId == user.ClusterId && evaluation.Response == ClusterResponse.YesInterested))))
            throw new ForbiddenException("You can only close requests associated with your cluster.");
        await wf.CloseAsync(t, ClosureReasons.ClosedByHead, dto.Reason.Trim());
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ReleaseRequestDetailDto> ApproveSlaExceptionAsync(int id, CancellationToken ct)
    {
        user.Require(RoleNames.Admin);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE release_requests SET UpdatedAt = UpdatedAt WHERE ReleaseRequestId = {id}", ct);
        var t = await wf.LoadAsync(id, ct);
        if (!TicketStateMachine.IsActive(t.Status))
            throw new BusinessRuleException("Only open requests can receive an SLA exception.");
        if (await db.AuditLogs.AnyAsync(audit => audit.ReleaseRequestId == id && audit.Action == "SlaExceptionApproved", ct))
            throw new ConflictException("This request already has its one-time SLA extension.");
        if (t.Evaluations.Any(evaluation => evaluation.Status == EvaluationStatus.Selected))
            throw new BusinessRuleException("An SLA exception is not needed after a cluster has selected the resource.");
        if (t.EvaluationDeadlineAt is null || Now < t.EvaluationDeadlineAt)
            throw new BusinessRuleException("An SLA exception can only be approved after the 14-day deadline.");

        var now = Now;
        var previousDeadline = t.EvaluationDeadlineAt.Value;
        t.EvaluationDeadlineAt = now.AddDays(14);
        t.SlaStatus = SlaStatus.WithinSla;
        t.UpdatedAt = now;
        audit.Record("ReleaseRequest", id, "SlaExceptionApproved",
            new { Deadline = previousDeadline },
            new { Deadline = t.EvaluationDeadlineAt, ExtensionDays = 14, ApprovedBy = user.DisplayName }, id);
        wf.AddEvent(t, "SlaExceptionApproved", "Admin approved one 14-day SLA extension for allocation results.");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    // ---------- Reopen ----------
    /// <summary>Never edits the closed ticket. Creates a NEW ticket (status REOPENED) that points back at the original.</summary>
    public async Task<ReleaseRequestDetailDto> ReopenAsync(int id, ReopenDto dto, CancellationToken ct)
    {
        user.Require(RoleNames.Head, RoleNames.Admin);
        DtoValidator.EnsureValid(dto);
        var creatorId = user.UserId ?? throw new ForbiddenException();
        var orig = await wf.LoadAsync(id, ct);
        if (orig.Status != TicketStatus.Closed) throw new BusinessRuleException("Only a CLOSED ticket can be reopened.");
        if (await db.ReleaseRequests.AnyAsync(r => r.ReopenedFromTicketId == id && TicketStateMachine.ActiveStatuses.Contains(r.Status), ct))
            throw new ConflictException("An active reopened ticket already exists for this ticket.");
        var clusterId = dto.RequestingClusterId ?? orig.InitiatingClusterId;
        if (!await db.Clusters.AnyAsync(c => c.Id == clusterId && c.IsActive, ct)) throw new RequestValidationException("requestingClusterId", "Unknown or inactive cluster.");

        var now = Now;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var t = new ReleaseRequest
        {
            TicketNumber = await numbers.NextAsync(now.Year, ct),
            EmployeeId = orig.EmployeeId, EmployeeName = orig.EmployeeName, EmployeeSource = orig.EmployeeSource,
            ReleaseDate = orig.ReleaseDate, ReasonForRelease = dto.Reason.Trim(), ReleaseCategoryId = orig.ReleaseCategoryId,
            RecommendedForNewAssignment = orig.RecommendedForNewAssignment, EmployeeAware = orig.EmployeeAware,
            EmployeeSkills = orig.EmployeeSkills, FeedbackProvided = orig.FeedbackProvided, DetailedFeedback = orig.DetailedFeedback,
            CommitmentMade = orig.CommitmentMade, FactoredInForecast = orig.FactoredInForecast, Grade = orig.Grade, Location = orig.Location,
            CurrentSupervisor = orig.CurrentSupervisor, CurrentProjectId = orig.CurrentProjectId, CurrentProjectName = orig.CurrentProjectName,
            Remarks = orig.Remarks, ReleaseLocation = orig.ReleaseLocation, Psl = orig.Psl, Band = orig.Band, BackfillRequired = orig.BackfillRequired,
            InitiatingClusterId = clusterId, Status = TicketStatus.Reopened, SlaStatus = SlaStatus.WithinSla, AssignedToHead = true,
            ReopenedFromTicketId = orig.ReleaseRequestId, CreatedBy = creatorId, CreatedByName = user.DisplayName, CreatedAt = now, UpdatedAt = now
        };
        db.ReleaseRequests.Add(t);
        await db.SaveChangesAsync(ct);

        db.TicketStatusHistory.Add(new TicketStatusHistory
        {
            ReleaseRequestId = t.ReleaseRequestId, EventType = "StatusChange", OldStatus = null, NewStatus = TicketStatus.Reopened,
            ChangedBy = creatorId, ChangedByName = user.DisplayName, ChangedAt = now, Reason = $"Reopened from {orig.TicketNumber}", Comments = dto.Reason
        });
        audit.Record("ReleaseRequest", t.ReleaseRequestId, "Reopened", null, new { From = orig.TicketNumber, dto.Reason, clusterId }, t.ReleaseRequestId);
        audit.Record("ReleaseRequest", orig.ReleaseRequestId, "ReopenedAs", null, new { NewTicket = t.TicketNumber }, orig.ReleaseRequestId);   // original is untouched; only audit gains a line

        // Rehire is never automatic: Head is asked to send the request to Competency.
        var extra = new Dictionary<string, string?> { ["PreviousTicket"] = orig.TicketNumber, ["Reason"] = dto.Reason };
        await notify.EnqueueAsync(NotificationType.Reopened, t, await notify.RoleEmailsAsync(RoleNames.Head), extra);
        await notify.EnqueueAsync(NotificationType.CompetencyNotification, t, await notify.RoleEmailsAsync(RoleNames.CompetencyPmo), extra);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(t.ReleaseRequestId, ct);
    }

    // ---------- Dashboard ----------
    public async Task<DashboardDto> DashboardAsync(CancellationToken ct)
    {
        var q = Visible(db.ReleaseRequests.AsNoTracking());
        var sla = await GetSlaAsync(ct);
        var counts = await q.GroupBy(t => t.Status).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
        int N(TicketStatus s) => counts.FirstOrDefault(c => c.Key == s)?.N ?? 0;
        var active = TicketStateMachine.ActiveStatuses;
        var cutoff = Now.AddDays(sla.AtRiskDays);

        return new DashboardDto(N(TicketStatus.Open), N(TicketStatus.Wip), N(TicketStatus.Closed), N(TicketStatus.Cancelled), N(TicketStatus.Reopened),
            await q.CountAsync(t => active.Contains(t.Status) && t.SlaStatus == SlaStatus.WithinSla && t.EvaluationDeadlineAt != null && t.EvaluationDeadlineAt <= cutoff, ct),
            await q.CountAsync(t => active.Contains(t.Status) && t.SlaStatus == SlaStatus.Breached, ct),
            await q.CountAsync(t => active.Contains(t.Status) && t.Evaluations.Any(e => e.Status == EvaluationStatus.Pending), ct),
            await q.CountAsync(t => active.Contains(t.Status) && t.Evaluations.Any(e => e.Status == EvaluationStatus.Interested || e.Status == EvaluationStatus.InterviewScheduled), ct),
            await q.CountAsync(t => active.Contains(t.Status) && t.Evaluations.Any(e => e.Status == EvaluationStatus.WaitingForResults), ct),
            await db.ClusterEvaluations.CountAsync(evaluation => evaluation.Status == EvaluationStatus.Selected &&
                q.Any(ticket => ticket.ReleaseRequestId == evaluation.ReleaseRequestId), ct),
            await db.ClusterEvaluations.CountAsync(evaluation => evaluation.Status == EvaluationStatus.Rejected &&
                q.Any(ticket => ticket.ReleaseRequestId == evaluation.ReleaseRequestId), ct));
    }

    // ---------- helpers ----------
    private async Task<SlaConfiguration> GetSlaAsync(CancellationToken ct)
        => await db.SlaConfigurations.AsNoTracking().FirstAsync(c => c.TicketType == SlaConfiguration.ReleaseRequest && c.IsActive, ct);

    private IQueryable<ReleaseRequest> Visible(IQueryable<ReleaseRequest> q)
    {
        if (user.IsInRole(RoleNames.Admin, RoleNames.Head, RoleNames.Pmo, RoleNames.CompetencyPmo)) return q;
        var uid = user.UserId; var cid = user.ClusterId;
        return q.Where(t => t.CreatedBy == uid ||
            (cid != null && (t.InitiatingClusterId == cid || t.CurrentOwnerClusterId == cid || t.Evaluations.Any(e => e.ClusterId == cid))));
    }

    private async Task EnsureVisibleAsync(int id, CancellationToken ct)
    {
        if (!await Visible(db.ReleaseRequests.AsNoTracking()).AnyAsync(t => t.ReleaseRequestId == id, ct))
            throw new NotFoundException($"Release request {id} was not found.");
    }

    private bool CanEdit(ReleaseRequest t) => user.IsInRole(RoleNames.Admin) ||
        (user.IsInRole(RoleNames.Pm, RoleNames.Sdm, RoleNames.Pmo) && (t.CreatedBy == user.UserId || (user.ClusterId != null && t.InitiatingClusterId == user.ClusterId)));
    private bool CanCancelRole(ReleaseRequest t) => user.IsInRole(RoleNames.Head, RoleNames.Admin) || CanEdit(t);

    private static T? ParseEnum<T>(string? s) where T : struct, Enum
        => Enum.TryParse<T>((s ?? "").Replace("_", ""), true, out var v) ? v : null;

    private ReleaseRequestDetailDto ToDetail(ReleaseRequest t, bool exceptionApproved)
    {
        var active = TicketStateMachine.IsActive(t.Status);
        var actions = new List<string>();
        if (active && CanEdit(t)) { actions.Add("edit"); }
        if (active && CanCancelRole(t)) actions.Add("cancel");
        if (active && (user.IsInRole(RoleNames.Admin) ||
            (user.IsInRole(RoleNames.Asdm) && user.ClusterId is not null && (user.ClusterId == t.InitiatingClusterId ||
                t.Evaluations.Any(evaluation => evaluation.ClusterId == user.ClusterId && evaluation.Response == ClusterResponse.YesInterested)))))
            actions.Add("close");
        if (active && user.IsInRole(RoleNames.Admin)) actions.Add("assign");
        if (active && user.IsInRole(RoleNames.Admin) && t.EvaluationDeadlineAt is not null &&
            Now >= t.EvaluationDeadlineAt && !exceptionApproved)
            actions.Add("approveSlaException");
        if (t.Status == TicketStatus.Closed && user.IsInRole(RoleNames.Head, RoleNames.Admin)) actions.Add("reopen");
        if (user.IsInRole(RoleNames.Head, RoleNames.Admin, RoleNames.Pmo)) actions.Add("viewAudit");
        if (active && (CanEdit(t) || user.IsInRole(RoleNames.Head, RoleNames.Admin))) actions.Add("attach");

        return new ReleaseRequestDetailDto
        {
            Id = t.ReleaseRequestId, TicketNumber = t.TicketNumber, Status = t.Status, SlaStatus = t.SlaStatus, EmployeeSource = t.EmployeeSource,
            EmployeeId = t.EmployeeId, EmployeeName = t.EmployeeName, Grade = t.Grade, Location = t.Location, CurrentSupervisor = t.CurrentSupervisor,
            CurrentProjectId = t.CurrentProjectId, CurrentProjectName = t.CurrentProjectName, Psl = t.Psl, Band = t.Band,
            ReleaseDate = t.ReleaseDate, ReleaseLocation = t.ReleaseLocation, ReasonForRelease = t.ReasonForRelease,
            Category = t.ReleaseCategory.Name, ReleaseCategoryId = t.ReleaseCategoryId,
            RecommendedForNewAssignment = t.RecommendedForNewAssignment, EmployeeAware = t.EmployeeAware, EmployeeSkills = t.EmployeeSkills,
            FeedbackProvided = t.FeedbackProvided, DetailedFeedback = t.DetailedFeedback, CommitmentMade = t.CommitmentMade,
            FactoredInForecast = t.FactoredInForecast, BackfillRequired = t.BackfillRequired, Remarks = t.Remarks,
            InitiatingCluster = t.InitiatingCluster.Code, OwnerCluster = t.CurrentOwnerCluster?.Code, OwnerName = t.CurrentOwnerUser?.DisplayName,
            AssignedToHead = t.AssignedToHead, CreatedByName = t.CreatedByName, CreatedAt = t.CreatedAt, UpdatedAt = t.UpdatedAt,
            EvaluationDeadlineAt = t.EvaluationDeadlineAt, SlaExceptionApproved = exceptionApproved,
            ClosedAt = t.ClosedAt, ClosedByName = t.ClosedByName, ClosureReason = t.ClosureReason,
            CancellationReason = t.CancellationReason, ReopenedFromTicketId = t.ReopenedFromTicketId, ReopenedFromTicketNumber = t.ReopenedFrom?.TicketNumber,
            Evaluations = t.Evaluations.OrderBy(e => e.ResponseSequence ?? int.MaxValue).ThenBy(e => e.Cluster.Code).Select(e => EvaluationService.ToDto(e, t, user)).ToList(),
            AllowedActions = actions
        };
    }
}
