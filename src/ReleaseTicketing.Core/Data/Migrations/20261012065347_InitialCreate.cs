using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ReleaseTicketing.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    AuditId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ReleaseRequestId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PerformedBy = table.Column<int>(type: "int", nullable: true),
                    PerformedByName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PerformedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.AuditId);
                });

            migrationBuilder.CreateTable(
                name: "employees",
                columns: table => new
                {
                    EmployeeId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Grade = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Supervisor = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProjectId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProjectName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Psl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Band = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Skills = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employees", x => x.EmployeeId);
                });

            migrationBuilder.CreateTable(
                name: "release_categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequiresClusterCirculation = table.Column<bool>(type: "bit", nullable: false),
                    RequiresInterview = table.Column<bool>(type: "bit", nullable: false),
                    RequiresCompetencyNotification = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sla_configuration",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TicketType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DurationDays = table.Column<int>(type: "int", nullable: false),
                    CalendarType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AutoCloseEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AutoCloseGraceHours = table.Column<int>(type: "int", nullable: false),
                    RemindersEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ReminderDays = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CancellationWindowWorkingDays = table.Column<int>(type: "int", nullable: false),
                    AtRiskDays = table.Column<int>(type: "int", nullable: false),
                    EscalationEmails = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sla_configuration", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ticket_counters",
                columns: table => new
                {
                    Year = table.Column<int>(type: "int", nullable: false),
                    LastValue = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_counters", x => x.Year);
                });

            migrationBuilder.CreateTable(
                name: "working_calendar",
                columns: table => new
                {
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    IsWorkingDay = table.Column<bool>(type: "bit", nullable: false),
                    HolidayName = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_working_calendar", x => x.Date);
                });

            migrationBuilder.CreateTable(
                name: "cluster_evaluations",
                columns: table => new
                {
                    EvaluationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReleaseRequestId = table.Column<int>(type: "int", nullable: false),
                    ClusterId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Response = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ResponseAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResponseBy = table.Column<int>(type: "int", nullable: true),
                    ResponseSequence = table.Column<int>(type: "int", nullable: true),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InterviewDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SelectionResult = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OwnerUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cluster_evaluations", x => x.EvaluationId);
                });

            migrationBuilder.CreateTable(
                name: "clusters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ManagerUserId = table.Column<int>(type: "int", nullable: true),
                    NotificationEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clusters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntraObjectId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ClusterId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_users_clusters_ClusterId",
                        column: x => x.ClusterId,
                        principalTable: "clusters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_requests",
                columns: table => new
                {
                    ReleaseRequestId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TicketNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EmployeeId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EmployeeName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmployeeSource = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ReleaseDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReasonForRelease = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReleaseCategoryId = table.Column<int>(type: "int", nullable: false),
                    RecommendedForNewAssignment = table.Column<bool>(type: "bit", nullable: false),
                    EmployeeAware = table.Column<bool>(type: "bit", nullable: false),
                    EmployeeSkills = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FeedbackProvided = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DetailedFeedback = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CommitmentMade = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FactoredInForecast = table.Column<bool>(type: "bit", nullable: false),
                    Grade = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CurrentSupervisor = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CurrentProjectId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CurrentProjectName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReleaseLocation = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Psl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Band = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BackfillRequired = table.Column<bool>(type: "bit", nullable: false),
                    InitiatingClusterId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SlaStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CurrentOwnerUserId = table.Column<int>(type: "int", nullable: true),
                    CurrentOwnerClusterId = table.Column<int>(type: "int", nullable: true),
                    AssignedToHead = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedByName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EvaluationStartAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EvaluationDeadlineAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedBy = table.Column<int>(type: "int", nullable: true),
                    ClosedByName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClosureReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReopenedFromTicketId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_requests", x => x.ReleaseRequestId);
                    table.ForeignKey(
                        name: "FK_release_requests_clusters_CurrentOwnerClusterId",
                        column: x => x.CurrentOwnerClusterId,
                        principalTable: "clusters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_release_requests_clusters_InitiatingClusterId",
                        column: x => x.InitiatingClusterId,
                        principalTable: "clusters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_release_requests_release_categories_ReleaseCategoryId",
                        column: x => x.ReleaseCategoryId,
                        principalTable: "release_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_release_requests_release_requests_ReopenedFromTicketId",
                        column: x => x.ReopenedFromTicketId,
                        principalTable: "release_requests",
                        principalColumn: "ReleaseRequestId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_release_requests_users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_release_requests_users_CurrentOwnerUserId",
                        column: x => x.CurrentOwnerUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_roles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_user_roles_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_roles_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notification_queue",
                columns: table => new
                {
                    NotificationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReleaseRequestId = table.Column<int>(type: "int", nullable: true),
                    Recipient = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DedupeKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_queue", x => x.NotificationId);
                    table.ForeignKey(
                        name: "FK_notification_queue_release_requests_ReleaseRequestId",
                        column: x => x.ReleaseRequestId,
                        principalTable: "release_requests",
                        principalColumn: "ReleaseRequestId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ticket_attachments",
                columns: table => new
                {
                    AttachmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReleaseRequestId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BlobPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FileType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    UploadedBy = table.Column<int>(type: "int", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_attachments", x => x.AttachmentId);
                    table.ForeignKey(
                        name: "FK_ticket_attachments_release_requests_ReleaseRequestId",
                        column: x => x.ReleaseRequestId,
                        principalTable: "release_requests",
                        principalColumn: "ReleaseRequestId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ticket_attachments_users_UploadedBy",
                        column: x => x.UploadedBy,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ticket_status_history",
                columns: table => new
                {
                    HistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReleaseRequestId = table.Column<int>(type: "int", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    OldStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    NewStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ChangedBy = table.Column<int>(type: "int", nullable: true),
                    ChangedByName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Comments = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_status_history", x => x.HistoryId);
                    table.ForeignKey(
                        name: "FK_ticket_status_history_release_requests_ReleaseRequestId",
                        column: x => x.ReleaseRequestId,
                        principalTable: "release_requests",
                        principalColumn: "ReleaseRequestId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notification_history",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NotificationId = table.Column<int>(type: "int", nullable: false),
                    AttemptedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notification_history_notification_queue_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "notification_queue",
                        principalColumn: "NotificationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "clusters",
                columns: new[] { "Id", "Code", "IsActive", "ManagerUserId", "Name", "NotificationEmail" },
                values: new object[,]
                {
                    { 1, "EY1", true, null, "Cluster EY1", null },
                    { 2, "EY2", true, null, "Cluster EY2", null },
                    { 3, "EY3", true, null, "Cluster EY3", null },
                    { 4, "EY4", true, null, "Cluster EY4", null },
                    { 5, "EY5", true, null, "Cluster EY5", null }
                });

            migrationBuilder.InsertData(
                table: "release_categories",
                columns: new[] { "Id", "Code", "IsActive", "Name", "RequiresClusterCirculation", "RequiresCompetencyNotification", "RequiresInterview" },
                values: new object[,]
                {
                    { 1, "NORMAL_RELEASE", true, "Normal release", true, true, true },
                    { 2, "RESIGNATION", true, "Resignation", false, false, false },
                    { 3, "PERFORMANCE", true, "Performance issues", false, false, false },
                    { 4, "MEDICAL", true, "Medical reasons", false, false, false },
                    { 5, "LONG_LEAVE", true, "Long leave", false, false, false }
                });

            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "Id", "Description", "Name" },
                values: new object[,]
                {
                    { 1, "PM", "PM" },
                    { 2, "SDM", "SDM" },
                    { 3, "PMO", "PMO" },
                    { 4, "ClusterManager", "ClusterManager" },
                    { 5, "Head", "Head" },
                    { 6, "CompetencyPmo", "CompetencyPmo" },
                    { 7, "Admin", "Admin" }
                });

            migrationBuilder.InsertData(
                table: "sla_configuration",
                columns: new[] { "Id", "AtRiskDays", "AutoCloseEnabled", "AutoCloseGraceHours", "CalendarType", "CancellationWindowWorkingDays", "DurationDays", "EscalationEmails", "IsActive", "ReminderDays", "RemindersEnabled", "TicketType" },
                values: new object[] { 1, 3, true, 24, "CALENDAR", 2, 14, "", true, "10,13", true, "RELEASE_REQUEST" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_EntityType_EntityId",
                table: "audit_logs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_ReleaseRequestId",
                table: "audit_logs",
                column: "ReleaseRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_cluster_evaluations_ClusterId",
                table: "cluster_evaluations",
                column: "ClusterId");

            migrationBuilder.CreateIndex(
                name: "IX_cluster_evaluations_OwnerUserId",
                table: "cluster_evaluations",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_cluster_evaluations_ReleaseRequestId_ClusterId",
                table: "cluster_evaluations",
                columns: new[] { "ReleaseRequestId", "ClusterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cluster_evaluations_ReleaseRequestId_ResponseSequence",
                table: "cluster_evaluations",
                columns: new[] { "ReleaseRequestId", "ResponseSequence" },
                unique: true,
                filter: "[ResponseSequence] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_cluster_evaluations_ResponseBy",
                table: "cluster_evaluations",
                column: "ResponseBy");

            migrationBuilder.CreateIndex(
                name: "IX_clusters_Code",
                table: "clusters",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_clusters_ManagerUserId",
                table: "clusters",
                column: "ManagerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_notification_history_NotificationId",
                table: "notification_history",
                column: "NotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_notification_queue_DedupeKey",
                table: "notification_queue",
                column: "DedupeKey",
                unique: true,
                filter: "[DedupeKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_notification_queue_ReleaseRequestId",
                table: "notification_queue",
                column: "ReleaseRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_notification_queue_Status_NextAttemptAt",
                table: "notification_queue",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_release_categories_Code",
                table: "release_categories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_release_requests_CreatedBy",
                table: "release_requests",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_release_requests_CurrentOwnerClusterId",
                table: "release_requests",
                column: "CurrentOwnerClusterId");

            migrationBuilder.CreateIndex(
                name: "IX_release_requests_CurrentOwnerUserId",
                table: "release_requests",
                column: "CurrentOwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_release_requests_EmployeeId",
                table: "release_requests",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_release_requests_InitiatingClusterId",
                table: "release_requests",
                column: "InitiatingClusterId");

            migrationBuilder.CreateIndex(
                name: "IX_release_requests_ReleaseCategoryId",
                table: "release_requests",
                column: "ReleaseCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_release_requests_ReopenedFromTicketId",
                table: "release_requests",
                column: "ReopenedFromTicketId");

            migrationBuilder.CreateIndex(
                name: "IX_release_requests_Status_EvaluationDeadlineAt",
                table: "release_requests",
                columns: new[] { "Status", "EvaluationDeadlineAt" });

            migrationBuilder.CreateIndex(
                name: "IX_release_requests_TicketNumber",
                table: "release_requests",
                column: "TicketNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_roles_Name",
                table: "roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sla_configuration_TicketType",
                table: "sla_configuration",
                column: "TicketType",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ticket_attachments_ReleaseRequestId",
                table: "ticket_attachments",
                column: "ReleaseRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_attachments_UploadedBy",
                table: "ticket_attachments",
                column: "UploadedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_status_history_ReleaseRequestId",
                table: "ticket_status_history",
                column: "ReleaseRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_RoleId",
                table: "user_roles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_users_ClusterId",
                table: "users",
                column: "ClusterId");

            migrationBuilder.CreateIndex(
                name: "IX_users_EntraObjectId",
                table: "users",
                column: "EntraObjectId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_cluster_evaluations_clusters_ClusterId",
                table: "cluster_evaluations",
                column: "ClusterId",
                principalTable: "clusters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_cluster_evaluations_release_requests_ReleaseRequestId",
                table: "cluster_evaluations",
                column: "ReleaseRequestId",
                principalTable: "release_requests",
                principalColumn: "ReleaseRequestId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_cluster_evaluations_users_OwnerUserId",
                table: "cluster_evaluations",
                column: "OwnerUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_cluster_evaluations_users_ResponseBy",
                table: "cluster_evaluations",
                column: "ResponseBy",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_clusters_users_ManagerUserId",
                table: "clusters",
                column: "ManagerUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_users_clusters_ClusterId",
                table: "users");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "cluster_evaluations");

            migrationBuilder.DropTable(
                name: "employees");

            migrationBuilder.DropTable(
                name: "notification_history");

            migrationBuilder.DropTable(
                name: "sla_configuration");

            migrationBuilder.DropTable(
                name: "ticket_attachments");

            migrationBuilder.DropTable(
                name: "ticket_counters");

            migrationBuilder.DropTable(
                name: "ticket_status_history");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "working_calendar");

            migrationBuilder.DropTable(
                name: "notification_queue");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "release_requests");

            migrationBuilder.DropTable(
                name: "release_categories");

            migrationBuilder.DropTable(
                name: "clusters");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
