using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using ReleaseTicketing.Api.Authorization;
using ReleaseTicketing.Api.Controllers;
using ReleaseTicketing.Core.Domain;
using ReleaseTicketing.Core.Dtos;
using ReleaseTicketing.Core.Exceptions;
using Xunit;

namespace ReleaseTicketing.Tests;

public class CreateTests
{
    [Fact]
    public async Task Valid_ticket_is_created_with_evaluations_history_audit_and_notifications()
    {
        using var h = new Harness();
        var t = await h.CreateTicketAsync();
        Assert.StartsWith("RR-2026-", t.TicketNumber);
        Assert.Equal(TicketStatus.Open, t.Status);
        Assert.Equal(EmployeeSource.Master, t.EmployeeSource);
        Assert.Equal(4, t.Evaluations.Count);                       // 5 clusters minus the initiating one (EY1)
        Assert.DoesNotContain(t.Evaluations, e => e.ClusterId == h.Ey1);
        Assert.Equal((DateTime?)h.Clock.GetUtcNow().UtcDateTime.AddDays(14), t.EvaluationDeadlineAt);

        await using var db = h.DbFactory();
        Assert.Single(db.TicketStatusHistory.Where(x => x.ReleaseRequestId == t.Id));
        Assert.Contains(db.AuditLogs, a => a.Action == "Created");
        Assert.Contains(db.NotificationQueue, n => n.Type == NotificationType.TicketCreated);
    }

    [Fact]
    public async Task Feedback_shorter_than_100_characters_is_rejected_by_the_backend()
    {
        using var h = new Harness();
        var dto = h.ValidDto(); dto.DetailedFeedback = "too short";
        var ex = await Assert.ThrowsAsync<RequestValidationException>(() => h.As(h.Pm()).Release.CreateAsync(dto, default));
        Assert.Contains("DetailedFeedback", ex.Errors.Keys);
        Assert.False(Validator.TryValidateObject(dto, new ValidationContext(dto), new List<ValidationResult>(), true));
    }

    [Fact]
    public async Task Feedback_of_exactly_100_non_whitespace_characters_is_accepted()
    {
        using var h = new Harness();
        var dto = h.ValidDto();
        dto.DetailedFeedback = new string('x', CreateReleaseRequestDto.MinFeedbackLength);

        var ticket = await h.As(h.Pm()).Release.CreateAsync(dto, default);

        Assert.Equal(CreateReleaseRequestDto.MinFeedbackLength, ticket.DetailedFeedback.Length);
    }

    [Fact]
    public async Task Missing_required_fields_are_rejected()
    {
        using var h = new Harness();
        var dto = h.ValidDto(); dto.EmployeeId = "";
        await Assert.ThrowsAsync<RequestValidationException>(() => h.As(h.Pm()).Release.CreateAsync(dto, default));
    }

    [Fact]
    public async Task Every_string_length_limit_is_enforced_by_the_service()
    {
        using var h = new Harness();
        var stringProperties = typeof(CreateReleaseRequestDto).GetProperties()
            .Select(property => (Property: property, Limit: (StringLengthAttribute?)Attribute.GetCustomAttribute(property, typeof(StringLengthAttribute))))
            .Where(item => item.Limit is not null);

        foreach (var (property, limit) in stringProperties)
        {
            var dto = h.ValidDto();
            property.SetValue(dto, new string('x', limit!.MaximumLength + 1));

            var error = await Assert.ThrowsAsync<RequestValidationException>(() => h.As(h.Pm()).Release.CreateAsync(dto, default));
            Assert.Contains(property.Name, error.Errors.Keys);
        }

        await AssertNoTicketCreatedAsync(h);
    }

    [Fact]
    public async Task Whitespace_required_strings_and_missing_ids_or_date_are_rejected()
    {
        using var h = new Harness();
        var requiredStringProperties = typeof(CreateReleaseRequestDto).GetProperties()
            .Where(property => Attribute.IsDefined(property, typeof(RequiredAttribute)) && property.PropertyType == typeof(string));

        foreach (var property in requiredStringProperties)
        {
            var dto = h.ValidDto();
            property.SetValue(dto, "   ");

            var error = await Assert.ThrowsAsync<RequestValidationException>(() => h.As(h.Pm()).Release.CreateAsync(dto, default));
            Assert.Contains(property.Name, error.Errors.Keys);
        }

        foreach (var propertyName in new[] { nameof(CreateReleaseRequestDto.ClusterId), nameof(CreateReleaseRequestDto.ReleaseCategoryId), nameof(CreateReleaseRequestDto.ReleaseDate) })
        {
            var dto = h.ValidDto();
            typeof(CreateReleaseRequestDto).GetProperty(propertyName)!.SetValue(dto, null);

            var error = await Assert.ThrowsAsync<RequestValidationException>(() => h.As(h.Pm()).Release.CreateAsync(dto, default));
            Assert.Contains(propertyName, error.Errors.Keys);
        }

        await AssertNoTicketCreatedAsync(h);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Unknown_or_inactive_cluster_and_category_are_rejected_without_side_effects(bool invalidCluster, bool invalidCategory)
    {
        using var h = new Harness();
        var dto = h.ValidDto();
        if (invalidCluster) dto.ClusterId = 999;
        if (invalidCategory) dto.ReleaseCategoryId = 999;

        var error = await Assert.ThrowsAsync<RequestValidationException>(() => h.As(h.Pm()).Release.CreateAsync(dto, default));
        Assert.Contains(invalidCluster ? "clusterId" : "releaseCategoryId", error.Errors.Keys);
        await AssertNoTicketCreatedAsync(h);

        if (invalidCluster)
        {
            await using var db = h.DbFactory();
            var cluster = await db.Clusters.SingleAsync(item => item.Id == h.Ey1);
            cluster.IsActive = false;
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<RequestValidationException>(() => h.As(h.Pm()).Release.CreateAsync(h.ValidDto(), default));
            await AssertNoTicketCreatedAsync(h);
        }
        else
        {
            await using var db = h.DbFactory();
            var category = await db.ReleaseCategories.SingleAsync(item => item.Id == 1);
            category.IsActive = false;
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<RequestValidationException>(() => h.As(h.Pm()).Release.CreateAsync(h.ValidDto(), default));
            await AssertNoTicketCreatedAsync(h);
        }
    }

    [Fact]
    public async Task Matching_master_data_is_trimmed_and_case_insensitive_without_override_audit()
    {
        using var h = new Harness();
        var dto = h.ValidDto();
        dto.EmployeeName = "  asha  ";
        dto.Grade = " g5 ";
        dto.Location = " CHENNAI ";
        dto.CurrentSupervisor = " ravi ";
        dto.CurrentProjectId = " p1 ";
        dto.CurrentProjectName = " proj ";
        dto.Psl = " psl1 ";
        dto.Band = " b2 ";

        var ticket = await h.As(h.Pm()).Release.CreateAsync(dto, default);

        Assert.Equal(EmployeeSource.Master, ticket.EmployeeSource);
        Assert.Equal("asha", ticket.EmployeeName);
        Assert.Equal("g5", ticket.Grade);
        await using var db = h.DbFactory();
        Assert.DoesNotContain(db.AuditLogs, audit => audit.Action == "EmployeeMasterOverride");
    }

    [Fact]
    public async Task Inactive_employee_master_record_is_treated_as_manual_entry()
    {
        using var h = new Harness();
        await using (var db = h.DbFactory())
        {
            var employee = await db.Employees.SingleAsync(item => item.EmployeeId == "E100");
            employee.IsActive = false;
            await db.SaveChangesAsync();
        }

        var ticket = await h.CreateTicketAsync();

        Assert.Equal(EmployeeSource.Manual, ticket.EmployeeSource);
        await using var check = h.DbFactory();
        Assert.Contains(check.AuditLogs, audit => audit.ReleaseRequestId == ticket.Id && audit.Action == "EmployeeManualEntry");
    }

    [Fact]
    public async Task Circulation_creation_queues_one_request_for_each_other_active_cluster()
    {
        using var h = new Harness();
        await using (var db = h.DbFactory())
        {
            foreach (var cluster in await db.Clusters.ToListAsync())
            {
                cluster.NotificationEmail = $"cluster{cluster.Id}@example.test";
            }
            await db.SaveChangesAsync();
        }

        var ticket = await h.CreateTicketAsync();

        Assert.Equal(4, ticket.Evaluations.Count);
        await using var check = h.DbFactory();
        var requests = await check.NotificationQueue.Where(item => item.ReleaseRequestId == ticket.Id && item.Type == NotificationType.ClusterEvaluationRequest).ToListAsync();
        Assert.Equal(7, requests.Count);
        Assert.Equal(new[] { "cluster2@example.test", "cluster3@example.test", "cluster4@example.test", "cluster5@example.test", "mgr2@x.com", "mgr3@x.com", "mgr4@x.com" },
            requests.Select(item => item.Recipient).OrderBy(recipient => recipient).ToArray());
        Assert.All(requests, item =>
        {
            Assert.Equal(NotificationStatus.Pending, item.Status);
            Assert.Contains(ticket.TicketNumber, item.PayloadJson);
            Assert.Contains("2026-10-19", item.PayloadJson);
        });
        Assert.Equal(2, await check.NotificationQueue.CountAsync(item => item.ReleaseRequestId == ticket.Id && item.Type == NotificationType.TicketCreated));
    }

    [Fact]
    public async Task Direct_release_category_assigns_head_without_cluster_notifications()
    {
        using var h = new Harness();

        var ticket = await h.CreateTicketAsync(categoryId: 2);

        Assert.Equal(TicketStatus.Wip, ticket.Status);
        Assert.True(ticket.AssignedToHead);
        Assert.Empty(ticket.Evaluations);
        await using var db = h.DbFactory();
        Assert.Equal(2, await db.NotificationQueue.CountAsync(item => item.ReleaseRequestId == ticket.Id && item.Type == NotificationType.TicketCreated));
        Assert.Empty(await db.NotificationQueue.Where(item => item.ReleaseRequestId == ticket.Id && item.Type == NotificationType.ClusterEvaluationRequest).ToListAsync());
        Assert.Contains(db.TicketStatusHistory, item => item.ReleaseRequestId == ticket.Id && item.NewStatus == TicketStatus.Wip);
    }

    [Fact]
    public async Task Authorized_creator_without_a_user_id_is_forbidden_before_persistence()
    {
        using var h = new Harness();
        var user = new TestUser { Roles = new HashSet<string> { RoleNames.Pm } };

        await Assert.ThrowsAsync<ForbiddenException>(() => h.As(user).Release.CreateAsync(h.ValidDto(), default));

        await AssertNoTicketCreatedAsync(h);
    }

    private static async Task AssertNoTicketCreatedAsync(Harness harness)
    {
        await using var db = harness.DbFactory();
        Assert.Empty(await db.ReleaseRequests.ToListAsync());
        Assert.Empty(await db.ClusterEvaluations.ToListAsync());
        Assert.Empty(await db.TicketStatusHistory.ToListAsync());
        Assert.Empty(await db.AuditLogs.ToListAsync());
        Assert.Empty(await db.NotificationQueue.ToListAsync());
    }

    [Fact]
    public async Task Employee_not_in_master_is_manual_and_audited()
    {
        using var h = new Harness();
        var t = await h.As(h.Pm()).Release.CreateAsync(h.ValidDto(employeeId: "NEW999"), default);
        Assert.Equal(EmployeeSource.Manual, t.EmployeeSource);
        await using var db = h.DbFactory();
        Assert.Contains(db.AuditLogs, a => a.Action == "EmployeeManualEntry");
    }

    [Fact]
    public async Task Overriding_master_data_is_marked_manual_and_audited()
    {
        using var h = new Harness();
        var dto = h.ValidDto(); dto.Grade = "G9";
        var t = await h.As(h.Pm()).Release.CreateAsync(dto, default);
        Assert.Equal(EmployeeSource.Manual, t.EmployeeSource);
        await using var db = h.DbFactory();
        Assert.Contains(db.AuditLogs, a => a.Action == "EmployeeMasterOverride");
    }

    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public async Task Exception_categories_skip_cluster_circulation(int categoryId)
    {
        using var h = new Harness();
        var t = await h.CreateTicketAsync(categoryId);
        Assert.Empty(t.Evaluations);
        Assert.Equal(TicketStatus.Wip, t.Status);
        Assert.True(t.AssignedToHead);
        Assert.Equal((DateTime?)h.Clock.GetUtcNow().UtcDateTime.AddDays(14), t.EvaluationDeadlineAt);
    }

    [Fact]
    public async Task User_without_a_creating_role_is_forbidden()
    {
        using var h = new Harness();
        await Assert.ThrowsAsync<ForbiddenException>(() => h.As(new TestUser { UserId = 999 }).Release.CreateAsync(h.ValidDto(), default));
    }

    [Fact]
    public async Task Asdm_and_admin_can_create_requests()
    {
        using var h = new Harness();
        var asdmTicket = await h.As(h.Manager(h.Ey2)).Release.CreateAsync(h.ValidDto(), default);
        var adminTicket = await h.As(h.Head()).Release.CreateAsync(h.ValidDto(employeeId: "NEW1"), default);
        Assert.NotEqual(asdmTicket.Id, adminTicket.Id);
    }

    [Fact]
    public async Task Local_dev_requests_get_a_default_pm_user()
    {
        using var h = new Harness();
        var svc = new CurrentUserService(h.DbFactory(), new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero)));

        await svc.InitializeAsync(new ClaimsPrincipal(new ClaimsIdentity()), "127.0.0.1", default);

        Assert.Equal(RoleNames.Pm, svc.Roles.Single());
        Assert.Equal("local-dev-user", svc.DisplayName);
        Assert.NotNull(svc.UserId);

        await using var db = h.DbFactory();
        var savedRoles = await db.UserRoles.Include(ur => ur.Role)
            .Where(ur => ur.UserId == svc.UserId)
            .Select(ur => ur.Role.Name)
            .ToListAsync();
        Assert.Equal(RoleNames.Pm, Assert.Single(savedRoles));
    }

    [Fact]
    public async Task Legacy_role_rows_and_memberships_are_consolidated_at_startup()
    {
        using var h = new Harness();
        await using var db = h.DbFactory();
        db.Roles.Single(role => role.Id == 2).Name = "SDM";
        db.Roles.AddRange(
            new Role { Id = 3, Name = "PMO", Description = "PMO" },
            new Role { Id = 4, Name = "ClusterManager", Description = "ClusterManager" },
            new Role { Id = 5, Name = "Head", Description = "Head" },
            new Role { Id = 6, Name = "CompetencyPmo", Description = "CompetencyPmo" });
        db.Users.Add(new User { Id = 999, EntraObjectId = "legacy-account", Email = "legacy@example.test", DisplayName = "Legacy", CreatedAt = h.Clock.GetUtcNow().UtcDateTime });
        db.UserRoles.AddRange(new UserRole { UserId = 999, RoleId = 4 }, new UserRole { UserId = 999, RoleId = 5 });
        await db.SaveChangesAsync();

        await LegacyRoleMigration.ApplyAsync(db);

        var roleNames = await db.Roles.Select(role => role.Name).ToArrayAsync();
        Assert.Equal(3, roleNames.Length);
        Assert.Contains("PM", roleNames);
        Assert.Contains("ASDM", roleNames);
        Assert.Contains("Admin", roleNames);
        var userRoles = await db.UserRoles.Include(join => join.Role).Where(join => join.UserId == 999).Select(join => join.Role.Name).ToArrayAsync();
        Assert.Equal(new[] { "Admin", "ASDM" }, userRoles.OrderBy(role => role).ToArray());
    }

    [Theory]
    [InlineData("SDM", "ASDM")]
    [InlineData("Manager", "ASDM")]
    [InlineData("ClusterManager", "ASDM")]
    [InlineData("Head", "Admin")]
    [InlineData("PMO", "Admin")]
    [InlineData("CompetencyPmo", "Admin")]
    public void Legacy_roles_normalize_to_one_of_the_three_supported_roles(string input, string expected)
    {
        Assert.Equal(expected, RoleNames.Normalize(input));
        Assert.Equal(3, RoleNames.All.Length);
    }
}

public class EvaluationTests
{
    private static async Task<List<ClusterEvaluation>> EvalsAsync(Harness h, int ticketId)
    { await using var db = h.DbFactory(); return await db.ClusterEvaluations.Include(e => e.Cluster).Where(e => e.ReleaseRequestId == ticketId).ToListAsync(); }

    private static async Task Respond(Harness h, ReleaseRequestDetailDto t, int cluster, ClusterResponse r)
    {
        var ev = (await EvalsAsync(h, t.Id)).Single(e => e.ClusterId == cluster);
        await h.As(h.Manager(cluster)).Eval.RespondAsync(t.Id, ev.EvaluationId, r, default);
    }

    [Fact]
    public async Task Available_resources_are_visible_to_all_roles_but_only_asdm_can_express_interest()
    {
        using var h = new Harness();
        var ticket = await h.CreateTicketAsync();

        var pmResources = await h.As(h.Pm()).Eval.GetAvailableResourcesAsync(new AvailableResourcesQuery(), default);
        var asdmResources = await h.As(h.Manager(h.Ey2)).Eval.GetAvailableResourcesAsync(new AvailableResourcesQuery(), default);
        var adminResources = await h.As(h.Head()).Eval.GetAvailableResourcesAsync(new AvailableResourcesQuery(), default);

        Assert.Contains(pmResources.Items, item => item.TicketId == ticket.Id && !item.CanShowInterest);
        Assert.Contains(asdmResources.Items, item => item.TicketId == ticket.Id && item.CanShowInterest && item.EvaluationId.HasValue);
        Assert.Contains(adminResources.Items, item => item.TicketId == ticket.Id && !item.CanShowInterest);

        var evaluationId = asdmResources.Items.Single(item => item.TicketId == ticket.Id).EvaluationId!.Value;
        await Assert.ThrowsAsync<ForbiddenException>(() => h.As(h.Pm()).Eval.RespondAsync(ticket.Id, evaluationId, ClusterResponse.YesInterested, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.As(h.Head()).Eval.RespondAsync(ticket.Id, evaluationId, ClusterResponse.YesInterested, default));
    }

    [Fact]
    public async Task Owned_tickets_are_filtered_by_cluster_or_request_creator_and_hidden_from_admin()
    {
        using var h = new Harness();
        var ticket = await h.CreateTicketAsync();
        await Respond(h, ticket, h.Ey2, ClusterResponse.YesInterested);
        await Respond(h, ticket, h.Ey3, ClusterResponse.YesInterested);

        var clusterItems = await h.As(h.Manager(h.Ey2)).Eval.GetOwnedAllocationTicketsAsync(default);
        var creatorItems = await h.As(h.Pm()).Eval.GetOwnedAllocationTicketsAsync(default);

        Assert.Equal(new[] { h.Ey2 }, clusterItems.Select(item => item.ClusterId));
        Assert.Equal(2, creatorItems.Count);
        await Assert.ThrowsAsync<ForbiddenException>(() => h.As(h.Head()).Eval.GetOwnedAllocationTicketsAsync(default));
        Assert.All(clusterItems, item => Assert.True(item.CanUpdateStatus));
        Assert.All(creatorItems, item => Assert.False(item.CanUpdateStatus));
    }

    [Fact]
    public async Task Allocation_status_changes_are_restricted_to_the_owning_cluster()
    {
        using var h = new Harness();
        var ticket = await h.CreateTicketAsync();
        await Respond(h, ticket, h.Ey2, ClusterResponse.YesInterested);
        var evaluation = (await EvalsAsync(h, ticket.Id)).Single(item => item.ClusterId == h.Ey2);

        await Assert.ThrowsAsync<ForbiddenException>(() => h.As(h.Manager(h.Ey3)).Eval.UpdateAllocationStatusAsync(
            ticket.Id, evaluation.EvaluationId, new UpdateAllocationStatusDto { Status = AllocationStatus.InterviewScheduled }, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.As(h.Pm()).Eval.UpdateAllocationStatusAsync(
            ticket.Id, evaluation.EvaluationId, new UpdateAllocationStatusDto { Status = AllocationStatus.InterviewScheduled }, default));
    }

    [Fact]
    public async Task Activity_feed_shows_other_cluster_updates_for_the_same_request()
    {
        using var h = new Harness();
        var ticket = await h.CreateTicketAsync();
        await Respond(h, ticket, h.Ey2, ClusterResponse.YesInterested);
        var evaluation = (await EvalsAsync(h, ticket.Id)).Single(item => item.ClusterId == h.Ey2);
        await h.As(h.Manager(h.Ey2)).Eval.UpdateAllocationStatusAsync(ticket.Id, evaluation.EvaluationId,
            new UpdateAllocationStatusDto { Status = AllocationStatus.InterviewScheduled }, default);

        var otherClusterFeed = await h.As(h.Manager(h.Ey3)).Activity.GetRecentAsync(default);
        var actorFeed = await h.As(h.Manager(h.Ey2)).Activity.GetRecentAsync(default);
        var creatorFeed = await h.As(h.Pm()).Activity.GetRecentAsync(default);
        var adminFeed = await h.As(h.Head()).Activity.GetRecentAsync(default);

        Assert.Contains(otherClusterFeed, item => item.TicketId == ticket.Id &&
            item.Message.Contains("updated allocation to Interview Scheduled", StringComparison.Ordinal));
        Assert.DoesNotContain(actorFeed, item => item.TicketId == ticket.Id);
        Assert.Contains(creatorFeed, item => item.TicketId == ticket.Id);
        Assert.Contains(adminFeed, item => item.TicketId == ticket.Id);
    }

    [Fact]
    public async Task Ticket_history_includes_each_cluster_response_and_allocation_status_change()
    {
        using var h = new Harness();
        var ticket = await h.CreateTicketAsync();
        await Respond(h, ticket, h.Ey2, ClusterResponse.YesInterested);
        await Respond(h, ticket, h.Ey3, ClusterResponse.NoNotInterested);
        var evaluation = (await EvalsAsync(h, ticket.Id)).Single(item => item.ClusterId == h.Ey2);
        var manager = h.As(h.Manager(h.Ey2)).Eval;
        await manager.UpdateAllocationStatusAsync(ticket.Id, evaluation.EvaluationId,
            new UpdateAllocationStatusDto { Status = AllocationStatus.InterviewScheduled }, default);
        await manager.UpdateAllocationStatusAsync(ticket.Id, evaluation.EvaluationId,
            new UpdateAllocationStatusDto { Status = AllocationStatus.WaitingForResults }, default);

        var history = await h.As(h.Head()).Release.GetHistoryAsync(ticket.Id, default);
        var clusterEvents = history.Where(item => item.EventType is "ClusterResponse" or "AllocationStatus").ToList();

        Assert.Equal(4, clusterEvents.Count);
        Assert.Contains(clusterEvents, item => item.EventType == "ClusterResponse" &&
            item.Reason!.Contains("EY2 expressed interest", StringComparison.Ordinal));
        Assert.Contains(clusterEvents, item => item.EventType == "ClusterResponse" &&
            item.Reason!.Contains("EY3 declined", StringComparison.Ordinal));
        Assert.Contains(clusterEvents, item => item.EventType == "AllocationStatus" &&
            item.Reason!.Contains("Interested to InterviewScheduled", StringComparison.Ordinal));
        Assert.Contains(clusterEvents, item => item.EventType == "AllocationStatus" &&
            item.Reason!.Contains("InterviewScheduled to WaitingForResults", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Positive_cluster_interest_moves_open_ticket_to_wip_but_decline_does_not()
    {
        using var h = new Harness();
        var ticket = await h.CreateTicketAsync();

        await Respond(h, ticket, h.Ey2, ClusterResponse.NoNotInterested);
        var afterDecline = await h.As(h.Pm()).Release.GetAsync(ticket.Id, default);
        Assert.Equal(TicketStatus.Open, afterDecline.Status);

        await Respond(h, ticket, h.Ey3, ClusterResponse.YesInterested);
        var afterInterest = await h.As(h.Pm()).Release.GetAsync(ticket.Id, default);

        Assert.Equal(TicketStatus.Wip, afterInterest.Status);
        var history = await h.As(h.Pm()).Release.GetHistoryAsync(ticket.Id, default);
        Assert.Contains(history, item => item.EventType == "StatusChange" &&
            item.OldStatus == TicketStatus.Open && item.NewStatus == TicketStatus.Wip &&
            item.Reason!.Contains("EY3 expressed interest", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Activity_feed_includes_system_updates_without_an_actor_id()
    {
        using var h = new Harness();
        var ticket = await h.CreateTicketAsync();
        await using (var db = h.DbFactory())
        {
            db.AuditLogs.Add(new AuditLog
            {
                EntityType = "ReleaseRequest",
                EntityId = ticket.Id.ToString(),
                ReleaseRequestId = ticket.Id,
                Action = "SlaBreached",
                NewValue = "{\"slaStatus\":\"Breached\"}",
                PerformedByName = "SYSTEM",
                PerformedAt = h.Clock.GetUtcNow().UtcDateTime
            });
            await db.SaveChangesAsync();
        }

        var feed = await h.As(h.Manager(h.Ey2)).Activity.GetRecentAsync(default);

        Assert.Contains(feed, item => item.TicketId == ticket.Id && item.Message == "The 14-day SLA has expired.");
    }

    [Fact]
    public async Task Allocation_updates_are_rejected_after_the_original_request_deadline()
    {
        using var h = new Harness();
        var ticket = await h.CreateTicketAsync();
        await Respond(h, ticket, h.Ey2, ClusterResponse.YesInterested);
        var evaluation = (await EvalsAsync(h, ticket.Id)).Single(item => item.ClusterId == h.Ey2);
        h.Clock.Advance(TimeSpan.FromDays(14));

        await Assert.ThrowsAsync<BusinessRuleException>(() => h.As(h.Manager(h.Ey2)).Eval.UpdateAllocationStatusAsync(
            ticket.Id, evaluation.EvaluationId, new UpdateAllocationStatusDto { Status = AllocationStatus.InterviewScheduled }, default));
    }

    [Fact]
    public async Task Cluster_manager_inbox_only_contains_pending_evaluations_for_their_cluster()
    {
        using var h = new Harness(); var ticket = await h.CreateTicketAsync();

        var inbox = await h.As(h.Manager(h.Ey2)).Eval.GetPendingForCurrentClusterAsync(default);

        var item = Assert.Single(inbox);
        Assert.Equal(ticket.Id, item.TicketId);
        Assert.Equal("EY2", (await EvalsAsync(h, ticket.Id)).Single(e => e.EvaluationId == item.EvaluationId).Cluster.Code);

        await h.As(h.Manager(h.Ey2)).Eval.RespondAsync(ticket.Id, item.EvaluationId, ClusterResponse.YesInterested, default);
        Assert.Empty(await h.As(h.Manager(h.Ey2)).Eval.GetPendingForCurrentClusterAsync(default));
        Assert.Single(await h.As(h.Manager(h.Ey3)).Eval.GetPendingForCurrentClusterAsync(default));
    }

    [Fact]
    public async Task Multiple_clusters_can_express_interest_without_exclusive_assignment()
    {
        using var h = new Harness(); var t = await h.CreateTicketAsync();
        await Respond(h, t, h.Ey2, ClusterResponse.YesInterested);
        await Respond(h, t, h.Ey3, ClusterResponse.YesInterested);

        var d = await h.As(h.Head()).Release.GetAsync(t.Id, default);
        Assert.Equal(TicketStatus.Wip, d.Status);
        Assert.Null(d.OwnerCluster);
        var ey2 = d.Evaluations.Single(e => e.ClusterCode == "EY2"); var ey3 = d.Evaluations.Single(e => e.ClusterCode == "EY3");
        Assert.Equal((1, EvaluationStatus.Interested), (ey2.ResponseSequence!.Value, ey2.Status));
        Assert.Equal((2, EvaluationStatus.Interested), (ey3.ResponseSequence!.Value, ey3.Status));
    }

    [Fact]
    public async Task No_response_is_recorded_and_does_not_assign()
    {
        using var h = new Harness(); var t = await h.CreateTicketAsync();
        await Respond(h, t, h.Ey2, ClusterResponse.NoNotInterested);
        var d = await h.As(h.Head()).Release.GetAsync(t.Id, default);
        Assert.Equal(TicketStatus.Open, d.Status);
        Assert.Null(d.OwnerCluster);
        Assert.Equal(EvaluationStatus.NotInterested, d.Evaluations.Single(e => e.ClusterCode == "EY2").Status);
    }

    [Fact]
    public async Task Cluster_cannot_respond_for_another_cluster()
    {
        using var h = new Harness(); var t = await h.CreateTicketAsync();
        var ey2 = (await EvalsAsync(h, t.Id)).Single(e => e.ClusterId == h.Ey2);
        await Assert.ThrowsAsync<ForbiddenException>(() => h.As(h.Manager(h.Ey3)).Eval.RespondAsync(t.Id, ey2.EvaluationId, ClusterResponse.YesInterested, default));
    }

    [Fact]
    public async Task Responding_twice_is_a_conflict()
    {
        using var h = new Harness(); var t = await h.CreateTicketAsync();
        await Respond(h, t, h.Ey2, ClusterResponse.NoNotInterested);
        await Assert.ThrowsAsync<ConflictException>(() => Respond(h, t, h.Ey2, ClusterResponse.YesInterested));
    }

    [Fact]
    public async Task Rejecting_one_cluster_does_not_close_other_cluster_allocation_tickets()
    {
        using var h = new Harness(); var t = await h.CreateTicketAsync();
        await Respond(h, t, h.Ey3, ClusterResponse.YesInterested);
        await Respond(h, t, h.Ey2, ClusterResponse.YesInterested);
        var e3 = (await EvalsAsync(h, t.Id)).Single(e => e.ClusterId == h.Ey3);

        await h.As(h.Manager(h.Ey3)).Eval.UpdateAllocationStatusAsync(t.Id, e3.EvaluationId,
            new UpdateAllocationStatusDto { Status = AllocationStatus.Rejected, RejectionReason = "Skills mismatch" }, default);

        var d = await h.As(h.Head()).Release.GetAsync(t.Id, default);
        Assert.Null(d.OwnerCluster);
        Assert.Equal(TicketStatus.Wip, d.Status);
        Assert.Equal(EvaluationStatus.Rejected, d.Evaluations.Single(e => e.ClusterCode == "EY3").Status);
        Assert.Equal(EvaluationStatus.Interested, d.Evaluations.Single(e => e.ClusterCode == "EY2").Status);
    }

    [Fact]
    public async Task Selection_closes_competing_allocation_tickets_but_keeps_request_active()
    {
        using var h = new Harness(); var t = await h.CreateTicketAsync();
        await Respond(h, t, h.Ey2, ClusterResponse.YesInterested);
        await Respond(h, t, h.Ey3, ClusterResponse.YesInterested);
        var e2 = (await EvalsAsync(h, t.Id)).Single(e => e.ClusterId == h.Ey2);
        await h.As(h.Manager(h.Ey2)).Eval.UpdateAllocationStatusAsync(t.Id, e2.EvaluationId,
            new UpdateAllocationStatusDto { Status = AllocationStatus.InterviewScheduled }, default);
        await h.As(h.Manager(h.Ey2)).Eval.UpdateAllocationStatusAsync(t.Id, e2.EvaluationId,
            new UpdateAllocationStatusDto { Status = AllocationStatus.WaitingForResults }, default);
        await h.As(h.Manager(h.Ey2)).Eval.UpdateAllocationStatusAsync(t.Id, e2.EvaluationId,
            new UpdateAllocationStatusDto { Status = AllocationStatus.Selected }, default);
        var d = await h.As(h.Head()).Release.GetAsync(t.Id, default);
        Assert.Equal(TicketStatus.Wip, d.Status);
        Assert.Equal("EY2", d.OwnerCluster);
        Assert.Equal(EvaluationStatus.Selected, d.Evaluations.Single(e => e.ClusterCode == "EY2").Status);
        Assert.Equal(EvaluationStatus.Closed, d.Evaluations.Single(e => e.ClusterCode == "EY3").Status);
        Assert.Equal(t.EvaluationDeadlineAt, d.EvaluationDeadlineAt);

        h.Clock.Advance(TimeSpan.FromDays(16));
        var slaResult = await h.As(h.System()).Sla.ProcessAsync(default);
        Assert.Equal(0, slaResult.AutoClosed);
        Assert.Equal(TicketStatus.Wip, (await h.As(h.Head()).Release.GetAsync(t.Id, default)).Status);
    }

    [Fact]
    public async Task All_clusters_decline_without_moving_or_closing_the_request()
    {
        using var h = new Harness(); var t = await h.CreateTicketAsync();
        foreach (var c in new[] { h.Ey2, h.Ey3, h.Ey4, h.Ey5 }) await Respond(h, t, c, ClusterResponse.NoNotInterested);
        var d = await h.As(h.Head()).Release.GetAsync(t.Id, default);
        Assert.False(d.AssignedToHead);
        Assert.Null(d.OwnerCluster);
        Assert.Equal(TicketStatus.Open, d.Status);
    }

    [Fact]
    public async Task Rejected_cluster_ticket_remains_closed_without_closing_the_request()
    {
        using var h = new Harness(); var t = await h.CreateTicketAsync();
        await Respond(h, t, h.Ey2, ClusterResponse.YesInterested);
        foreach (var c in new[] { h.Ey3, h.Ey4, h.Ey5 }) await Respond(h, t, c, ClusterResponse.NoNotInterested);
        var e2 = (await EvalsAsync(h, t.Id)).Single(e => e.ClusterId == h.Ey2);
        await h.As(h.Manager(h.Ey2)).Eval.UpdateAllocationStatusAsync(t.Id, e2.EvaluationId,
            new UpdateAllocationStatusDto { Status = AllocationStatus.Rejected, RejectionReason = "No fit" }, default);
        var d = await h.As(h.Head()).Release.GetAsync(t.Id, default);
        Assert.False(d.AssignedToHead); Assert.Equal(TicketStatus.Wip, d.Status);
        Assert.Equal(EvaluationStatus.Rejected, d.Evaluations.Single(e => e.ClusterCode == "EY2").Status);
    }

    [Fact]
    public async Task Rejection_requires_a_reason()
    {
        using var h = new Harness(); var t = await h.CreateTicketAsync();
        await Respond(h, t, h.Ey2, ClusterResponse.YesInterested);
        var e2 = (await EvalsAsync(h, t.Id)).Single(e => e.ClusterId == h.Ey2);
        await Assert.ThrowsAsync<RequestValidationException>(() => h.As(h.Manager(h.Ey2)).Eval.CompleteAsync(t.Id, e2.EvaluationId, new CompleteEvaluationDto { SelectionResult = SelectionResult.NotSelected }, default));
    }
}

public class CancelReopenTests
{
    [Fact]
    public async Task Cancel_within_two_working_days_succeeds_and_is_audited()
    {
        using var h = new Harness(); var t = await h.CreateTicketAsync();
        h.Clock.Advance(TimeSpan.FromDays(2));   // Mon -> Wed
        var d = await h.As(h.Pm()).Release.CancelAsync(t.Id, new CancelDto { Reason = "Internal opportunity found", OpportunityIdentifiedOn = new DateOnly(2026, 10, 5) }, default);
        Assert.Equal(TicketStatus.Cancelled, d.Status);
        await using var db = h.DbFactory();
        Assert.Contains(db.AuditLogs, a => a.Action == "Cancelled");
        Assert.Contains(db.TicketStatusHistory, x => x.NewStatus == TicketStatus.Cancelled);
    }

    [Fact]
    public async Task Cancel_after_the_window_is_rejected()
    {
        using var h = new Harness(); var t = await h.CreateTicketAsync();
        h.Clock.Advance(TimeSpan.FromDays(4));   // Mon -> Fri = 4 working days
        await Assert.ThrowsAsync<BusinessRuleException>(() => h.As(h.Pm()).Release.CancelAsync(t.Id, new CancelDto { Reason = "Internal opportunity found", OpportunityIdentifiedOn = new DateOnly(2026, 10, 5) }, default));
    }

    [Fact]
    public async Task Weekend_and_holidays_do_not_count_as_working_days()
    {
        using var h = new Harness(); var t = await h.CreateTicketAsync();
        await using (var db = h.DbFactory()) { db.WorkingCalendar.Add(new WorkingDay { Date = new DateOnly(2026, 10, 6), IsWorkingDay = false, HolidayName = "Test holiday" }); await db.SaveChangesAsync(); }
        h.Clock.Advance(TimeSpan.FromDays(3));   // Thu; Tue is a holiday -> Wed, Thu = 2 working days
        var d = await h.As(h.Pm()).Release.CancelAsync(t.Id, new CancelDto { Reason = "Internal opportunity found", OpportunityIdentifiedOn = new DateOnly(2026, 10, 5) }, default);
        Assert.Equal(TicketStatus.Cancelled, d.Status);
    }

    [Fact]
    public async Task Reopen_creates_a_new_ticket_and_leaves_the_original_closed()
    {
        using var h = new Harness(); var t = await h.CreateTicketAsync();
        var closed = await h.As(h.Head()).Release.CloseAsync(t.Id, new CloseDto { Reason = "Offboarded" }, default);
        Assert.Equal(TicketStatus.Closed, closed.Status);

        var again = await h.As(h.Head()).Release.ReopenAsync(t.Id, new ReopenDto { Reason = "Cluster EY3 has new demand", RequestingClusterId = h.Ey3 }, default);
        Assert.NotEqual(t.TicketNumber, again.TicketNumber);
        Assert.Equal(TicketStatus.Reopened, again.Status);
        Assert.Equal(t.TicketNumber, again.ReopenedFromTicketNumber);
        var original = await h.As(h.Head()).Release.GetAsync(t.Id, default);
        Assert.Equal(TicketStatus.Closed, original.Status);
        await Assert.ThrowsAsync<ConflictException>(() => h.As(h.Head()).Release.ReopenAsync(t.Id, new ReopenDto { Reason = "Second attempt here" }, default));
    }

    [Fact]
    public async Task Only_closed_tickets_can_be_reopened()
    {
        using var h = new Harness(); var t = await h.CreateTicketAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() => h.As(h.Head()).Release.ReopenAsync(t.Id, new ReopenDto { Reason = "Too early to do this" }, default));
    }
}

public class SlaTests
{
    private static async Task RespondToCluster(Harness h, ReleaseRequestDetailDto ticket, int clusterId)
    {
        await using var db = h.DbFactory();
        var evaluationId = await db.ClusterEvaluations
            .Where(item => item.ReleaseRequestId == ticket.Id && item.ClusterId == clusterId)
            .Select(item => item.EvaluationId)
            .SingleAsync();
        await h.As(h.Manager(clusterId)).Eval.RespondAsync(ticket.Id, evaluationId, ClusterResponse.YesInterested, default);
    }

    [Fact]
    public async Task Reminder_is_queued_once_on_day_10()
    {
        using var h = new Harness(); await h.CreateTicketAsync();
        h.Clock.Advance(TimeSpan.FromDays(10).Add(TimeSpan.FromHours(1)));
        var first = await h.As(h.System()).Sla.ProcessAsync(default);
        Assert.True(first.RemindersQueued > 0);
        var second = await h.As(h.System()).Sla.ProcessAsync(default);
        Assert.Equal(0, second.RemindersQueued);   // dedupe: not sent again
    }

    [Fact]
    public async Task Breach_marks_SLA_breached_but_does_not_close_before_the_cutoff()
    {
        using var h = new Harness(); var t = await h.CreateTicketAsync();
        h.Clock.Advance(TimeSpan.FromDays(14).Add(TimeSpan.FromHours(1)));
        var r = await h.As(h.System()).Sla.ProcessAsync(default);
        Assert.Equal(1, r.Breached); Assert.Equal(0, r.AutoClosed);
        var d = await h.As(h.Head()).Release.GetAsync(t.Id, default);
        Assert.Equal(SlaStatus.Breached, d.SlaStatus); Assert.Equal(TicketStatus.Open, d.Status);
        await using var db = h.DbFactory();
        Assert.Contains(db.NotificationQueue, n => n.Type == NotificationType.SlaBreach);
    }

    [Fact]
    public async Task An_expired_request_stays_active_until_an_asdm_manager_closes_it()
    {
        using var h = new Harness(); var t = await h.CreateTicketAsync();
        await RespondToCluster(h, t, h.Ey2);
        h.Clock.Advance(TimeSpan.FromDays(15));
        var r = await h.As(h.System()).Sla.ProcessAsync(default);
        Assert.Equal(0, r.AutoClosed);
        var beforeClose = await h.As(h.Head()).Release.GetAsync(t.Id, default);
        Assert.Equal(TicketStatus.Wip, beforeClose.Status);
        Assert.Equal(SlaStatus.Breached, beforeClose.SlaStatus);

        var d = await h.As(h.Manager(h.Ey2)).Release.CloseAsync(t.Id,
            new CloseDto { Reason = "ASDM confirmed the employee should be released." }, default);
        Assert.Equal(TicketStatus.Closed, d.Status);
        Assert.Equal(ClosureReasons.ClosedByHead, d.ClosureReason);
        Assert.Equal("mgr2", d.ClosedByName);
    }

    [Fact]
    public async Task Admin_can_approve_one_fourteen_day_extension_and_unresolved_ticket_closes_at_its_end()
    {
        using var h = new Harness(); var t = await h.CreateTicketAsync();
        h.Clock.Advance(TimeSpan.FromDays(14));
        var approved = await h.As(h.Head()).Release.ApproveSlaExceptionAsync(t.Id, default);
        Assert.True(approved.SlaExceptionApproved);
        Assert.Equal(h.Clock.GetUtcNow().UtcDateTime.AddDays(14), approved.EvaluationDeadlineAt);
        Assert.Equal(SlaStatus.WithinSla, approved.SlaStatus);
        await Assert.ThrowsAsync<ConflictException>(() => h.As(h.Head()).Release.ApproveSlaExceptionAsync(t.Id, default));

        h.Clock.Advance(TimeSpan.FromDays(14).Add(TimeSpan.FromMinutes(1)));
        var result = await h.As(h.System()).Sla.ProcessAsync(default);
        Assert.Equal(1, result.AutoClosed);
        var d = await h.As(h.Head()).Release.GetAsync(t.Id, default);
        Assert.Equal(TicketStatus.Closed, d.Status);
        Assert.Equal(ClosureReasons.SlaExpired, d.ClosureReason);
        Assert.Equal("SYSTEM", d.ClosedByName);
    }

    [Fact]
    public async Task Only_admin_can_approve_an_sla_exception_and_unrelated_asdm_cannot_close()
    {
        using var h = new Harness(); var t = await h.CreateTicketAsync();
        h.Clock.Advance(TimeSpan.FromDays(14));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.As(h.Manager(h.Ey2)).Release.ApproveSlaExceptionAsync(t.Id, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.As(h.Manager(h.Ey5)).Release.CloseAsync(t.Id,
            new CloseDto { Reason = "Not my cluster." }, default));
    }
}

public class DashboardTests
{
    private static async Task<ClusterEvaluation> EvaluationAsync(Harness h, int ticketId, int clusterId)
    {
        await using var db = h.DbFactory();
        return await db.ClusterEvaluations.SingleAsync(item => item.ReleaseRequestId == ticketId && item.ClusterId == clusterId);
    }

    [Fact]
    public async Task Dashboard_counts_allocated_and_released_cluster_outcomes()
    {
        using var h = new Harness();
        var allocated = await h.CreateTicketAsync();
        var released = await h.As(h.Pm()).Release.CreateAsync(h.ValidDto(employeeId: "E200"), default);
        var allocatedEvaluation = await EvaluationAsync(h, allocated.Id, h.Ey2);
        var releasedEvaluation = await EvaluationAsync(h, released.Id, h.Ey3);
        await h.As(h.Manager(h.Ey2)).Eval.RespondAsync(allocated.Id, allocatedEvaluation.EvaluationId, ClusterResponse.YesInterested, default);
        await h.As(h.Manager(h.Ey3)).Eval.RespondAsync(released.Id, releasedEvaluation.EvaluationId, ClusterResponse.YesInterested, default);
        var allocatedService = h.As(h.Manager(h.Ey2)).Eval;
        await allocatedService.UpdateAllocationStatusAsync(allocated.Id, allocatedEvaluation.EvaluationId,
            new UpdateAllocationStatusDto { Status = AllocationStatus.InterviewScheduled }, default);
        await allocatedService.UpdateAllocationStatusAsync(allocated.Id, allocatedEvaluation.EvaluationId,
            new UpdateAllocationStatusDto { Status = AllocationStatus.WaitingForResults }, default);
        await allocatedService.UpdateAllocationStatusAsync(allocated.Id, allocatedEvaluation.EvaluationId,
            new UpdateAllocationStatusDto { Status = AllocationStatus.Selected }, default);
        await h.As(h.Manager(h.Ey3)).Eval.UpdateAllocationStatusAsync(released.Id, releasedEvaluation.EvaluationId,
            new UpdateAllocationStatusDto { Status = AllocationStatus.Rejected, RejectionReason = "Skills mismatch" }, default);

        var dashboard = await h.As(h.Head()).Release.DashboardAsync(default);

        Assert.Equal(1, dashboard.AllocatedCount);
        Assert.Equal(1, dashboard.ReleasedCount);
    }
}

public class LocalDemoScenarioTests
{
    [Fact]
    public async Task Demo_scenarios_are_idempotent_and_closed_example_has_approved_extension_audit()
    {
        using var h = new Harness();
        await using var db = h.DbFactory();
        var service = new LocalDemoScenarioService(db, h.Pm(), h.Clock);

        var first = await service.SeedAsync(default);
        var second = await service.SeedAsync(default);

        Assert.Equal(4, first.Count);
        Assert.Equal(first.Select(item => item.TicketId).OrderBy(id => id),
            second.Select(item => item.TicketId).OrderBy(id => id));
        var nearDeadlineScenario = first.Single(item => item.TicketNumber.EndsWith("-D13", StringComparison.Ordinal));
        Assert.Equal(5, await db.TicketStatusHistory.CountAsync(item =>
            item.ReleaseRequestId == nearDeadlineScenario.TicketId &&
            (item.EventType == "ClusterResponse" || item.EventType == "AllocationStatus")));
        var closedScenario = first.Single(item => item.TicketNumber.EndsWith("-CLOSED", StringComparison.Ordinal));
        var closedRequest = await db.ReleaseRequests.SingleAsync(item => item.ReleaseRequestId == closedScenario.TicketId);
        Assert.True(closedRequest.ClosedAt.HasValue);
        Assert.Equal(closedRequest.EvaluationDeadlineAt, closedRequest.ClosedAt);
        Assert.Equal(1, await db.AuditLogs.CountAsync(audit =>
            audit.ReleaseRequestId == closedRequest.ReleaseRequestId && audit.Action == "SlaExceptionApproved"));
        Assert.Equal(1, await db.AuditLogs.CountAsync(audit =>
            audit.ReleaseRequestId == closedRequest.ReleaseRequestId && audit.Action == "Closed"));
    }
}

public class AttachmentTests
{
    [Fact]
    public async Task Disallowed_extension_and_oversized_files_are_rejected()
    {
        using var h = new Harness(); var t = await h.CreateTicketAsync();
        var svc = h.As(h.Pm()).Attach;
        await Assert.ThrowsAsync<RequestValidationException>(() => svc.UploadAsync(t.Id, "SOFTLOCK", "virus.exe", "application/octet-stream", 10, new MemoryStream(new byte[10]), default));
        await Assert.ThrowsAsync<RequestValidationException>(() => svc.UploadAsync(t.Id, "SOFTLOCK", "big.pdf", "application/pdf", 50L * 1024 * 1024, new MemoryStream(new byte[10]), default));
        await Assert.ThrowsAsync<RequestValidationException>(() => svc.UploadAsync(t.Id, "NOPE", "a.pdf", "application/pdf", 10, new MemoryStream(new byte[10]), default));
    }

    [Fact]
    public async Task Valid_upload_is_stored_with_generated_blob_path_and_audited()
    {
        using var h = new Harness(); var t = await h.CreateTicketAsync();
        var a = await h.As(h.Pm()).Attach.UploadAsync(t.Id, "softlock", "../../evil name.pdf", "application/pdf", 4, new MemoryStream([1, 2, 3, 4]), default);
        Assert.Equal("evil name.pdf", a.FileName);   // path parts stripped
        Assert.Single(h.Blobs.Files);
        Assert.StartsWith($"release-requests/{t.TicketNumber}/", h.Blobs.Files.Keys.Single());
        await using var db = h.DbFactory();
        Assert.Contains(db.AuditLogs, x => x.Action == "AttachmentUploaded");
    }

    [Fact]
    public async Task User_without_access_cannot_list_attachments()
    {
        using var h = new Harness(); var t = await h.CreateTicketAsync();
        var stranger = new TestUser { UserId = 999, DisplayName = "x", Roles = new HashSet<string> { RoleNames.Pm } };
        await Assert.ThrowsAsync<NotFoundException>(() => h.As(stranger).Attach.ListAsync(t.Id, default));
    }
}

public class AuthorizationTests
{
    [Fact]
    public async Task Audit_log_is_only_for_Head_Admin_PMO()
    {
        using var h = new Harness(); var t = await h.CreateTicketAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => h.As(h.Pm()).Release.GetAuditAsync(t.Id, default));
        var rows = await h.As(h.Head()).Release.GetAuditAsync(t.Id, default);
        Assert.NotEmpty(rows);
    }

    [Fact]
    public async Task Unrelated_user_cannot_see_a_ticket()
    {
        using var h = new Harness(); var t = await h.CreateTicketAsync();
        var stranger = new TestUser { UserId = 999, DisplayName = "x", Roles = new HashSet<string> { RoleNames.Pm } };
        await Assert.ThrowsAsync<NotFoundException>(() => h.As(stranger).Release.GetAsync(t.Id, default));
    }

    [Fact]
    public async Task Audit_and_history_rows_cannot_be_modified()
    {
        using var h = new Harness(); await h.CreateTicketAsync();
        await using var db = h.DbFactory();
        var row = await db.AuditLogs.FirstAsync(); row.Action = "tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Illegal_state_transition_is_refused()
    {
        Assert.False(TicketStateMachine.CanTransition(TicketStatus.Closed, TicketStatus.Open));
        Assert.False(TicketStateMachine.CanTransition(TicketStatus.Cancelled, TicketStatus.Wip));
        Assert.True(TicketStateMachine.CanTransition(TicketStatus.Open, TicketStatus.Cancelled));
        await Task.CompletedTask;
    }
}
