using Microsoft.EntityFrameworkCore;
using ReleaseTicketing.Core.Domain;

namespace ReleaseTicketing.Core.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Cluster> Clusters => Set<Cluster>();
    public DbSet<ReleaseCategory> ReleaseCategories => Set<ReleaseCategory>();
    public DbSet<ReleaseRequest> ReleaseRequests => Set<ReleaseRequest>();
    public DbSet<ClusterEvaluation> ClusterEvaluations => Set<ClusterEvaluation>();
    public DbSet<TicketStatusHistory> TicketStatusHistory => Set<TicketStatusHistory>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<TicketAttachment> TicketAttachments => Set<TicketAttachment>();
    public DbSet<NotificationQueueItem> NotificationQueue => Set<NotificationQueueItem>();
    public DbSet<NotificationHistory> NotificationHistory => Set<NotificationHistory>();
    public DbSet<SlaConfiguration> SlaConfigurations => Set<SlaConfiguration>();
    public DbSet<WorkingDay> WorkingCalendar => Set<WorkingDay>();
    public DbSet<TicketCounter> TicketCounters => Set<TicketCounter>();

    protected override void ConfigureConventions(ModelConfigurationBuilder cb)
    {
        // Store every enum as readable text ("WIP") instead of a number.
        cb.Properties<Enum>().HaveConversion<string>().HaveMaxLength(40);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Role>(e => { e.ToTable("roles"); e.Property(x => x.Name).HasMaxLength(50); e.HasIndex(x => x.Name).IsUnique(); });

        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.Property(x => x.EntraObjectId).HasMaxLength(64);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.HasIndex(x => x.EntraObjectId).IsUnique();
            e.HasOne<Cluster>().WithMany().HasForeignKey(x => x.ClusterId);
        });

        b.Entity<UserRole>(e =>
        {
            e.ToTable("user_roles");
            e.HasKey(x => new { x.UserId, x.RoleId });
            e.HasOne(x => x.User).WithMany(u => u.UserRoles).HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId);
        });

        b.Entity<Employee>(e => { e.ToTable("employees"); e.HasKey(x => x.EmployeeId); e.Property(x => x.EmployeeId).HasMaxLength(20); e.Property(x => x.Name).HasMaxLength(200); });

        b.Entity<Cluster>(e =>
        {
            e.ToTable("clusters");
            e.Property(x => x.Code).HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.NotificationEmail).HasMaxLength(256);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ManagerUserId);
        });

        b.Entity<ReleaseCategory>(e => { e.ToTable("release_categories"); e.Property(x => x.Code).HasMaxLength(40); e.HasIndex(x => x.Code).IsUnique(); });

        b.Entity<ReleaseRequest>(e =>
        {
            e.ToTable("release_requests");
            e.Property(x => x.TicketNumber).HasMaxLength(20);
            e.Property(x => x.EmployeeId).HasMaxLength(20);
            e.HasIndex(x => x.TicketNumber).IsUnique();
            e.HasIndex(x => x.EmployeeId);
            e.HasIndex(x => new { x.Status, x.EvaluationDeadlineAt });   // used by the SLA job
            e.HasIndex(x => x.InitiatingClusterId);
            e.HasIndex(x => x.CurrentOwnerClusterId);
            e.HasIndex(x => x.CreatedBy);
            e.HasOne(x => x.ReleaseCategory).WithMany().HasForeignKey(x => x.ReleaseCategoryId);
            e.HasOne(x => x.InitiatingCluster).WithMany().HasForeignKey(x => x.InitiatingClusterId);
            e.HasOne(x => x.CurrentOwnerCluster).WithMany().HasForeignKey(x => x.CurrentOwnerClusterId);
            e.HasOne(x => x.CurrentOwnerUser).WithMany().HasForeignKey(x => x.CurrentOwnerUserId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy);
            e.HasOne(x => x.ReopenedFrom).WithMany().HasForeignKey(x => x.ReopenedFromTicketId);
        });

        b.Entity<ClusterEvaluation>(e =>
        {
            e.ToTable("cluster_evaluations");
            e.HasOne(x => x.ReleaseRequest).WithMany(r => r.Evaluations).HasForeignKey(x => x.ReleaseRequestId);
            e.HasOne(x => x.Cluster).WithMany().HasForeignKey(x => x.ClusterId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ResponseBy);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId);
            e.HasIndex(x => new { x.ReleaseRequestId, x.ClusterId }).IsUnique();
            // Safety net: even if the lock logic had a bug, the DB refuses two evaluations with the same sequence.
            e.HasIndex(x => new { x.ReleaseRequestId, x.ResponseSequence }).IsUnique().HasFilter("[ResponseSequence] IS NOT NULL");
        });

        b.Entity<TicketStatusHistory>(e =>
        {
            e.ToTable("ticket_status_history");
            e.HasKey(x => x.HistoryId);
            e.Property(x => x.EventType).HasMaxLength(40);
            e.HasOne(x => x.ReleaseRequest).WithMany(r => r.History).HasForeignKey(x => x.ReleaseRequestId);
            e.HasIndex(x => x.ReleaseRequestId);
        });

        b.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_logs");
            e.HasKey(x => x.AuditId);
            e.Property(x => x.EntityType).HasMaxLength(60);
            e.Property(x => x.EntityId).HasMaxLength(60);
            e.Property(x => x.Action).HasMaxLength(60);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => x.ReleaseRequestId);
        });

        b.Entity<TicketAttachment>(e =>
        {
            e.ToTable("ticket_attachments");
            e.Property(x => x.Kind).HasMaxLength(30);
            e.Property(x => x.BlobPath).HasMaxLength(500);
            e.HasOne(x => x.ReleaseRequest).WithMany(r => r.Attachments).HasForeignKey(x => x.ReleaseRequestId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UploadedBy);
        });

        b.Entity<NotificationQueueItem>(e =>
        {
            e.ToTable("notification_queue");
            e.HasKey(x => x.NotificationId);
            e.Property(x => x.Recipient).HasMaxLength(256);
            e.Property(x => x.DedupeKey).HasMaxLength(300);
            e.HasOne(x => x.ReleaseRequest).WithMany().HasForeignKey(x => x.ReleaseRequestId);
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.HasIndex(x => x.DedupeKey).IsUnique().HasFilter("[DedupeKey] IS NOT NULL");
        });

        b.Entity<NotificationHistory>(e =>
        {
            e.ToTable("notification_history");
            e.HasOne(x => x.Notification).WithMany().HasForeignKey(x => x.NotificationId);
        });

        b.Entity<SlaConfiguration>(e =>
        {
            e.ToTable("sla_configuration");
            e.Property(x => x.TicketType).HasMaxLength(40);
            e.Property(x => x.CalendarType).HasMaxLength(20);
            e.HasIndex(x => x.TicketType).IsUnique();
        });

        b.Entity<WorkingDay>(e => { e.ToTable("working_calendar"); e.HasKey(x => x.Date); });
        b.Entity<TicketCounter>(e => { e.ToTable("ticket_counters"); e.HasKey(x => x.Year); e.Property(x => x.Year).ValueGeneratedNever(); });

        Seed(b);

        // Nothing is ever cascade-deleted: history must survive.
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;
    }

    private static void Seed(ModelBuilder b)
    {
        b.Entity<Role>().HasData(
            new Role { Id = 1, Name = RoleNames.Pm, Description = RoleNames.Pm },
            new Role { Id = 2, Name = RoleNames.Asdm, Description = RoleNames.Asdm },
            new Role { Id = 7, Name = RoleNames.Admin, Description = RoleNames.Admin });

        b.Entity<Cluster>().HasData(Enumerable.Range(1, 5).Select(i => new Cluster { Id = i, Code = $"EY{i}", Name = $"Cluster EY{i}", IsActive = true }));

        b.Entity<ReleaseCategory>().HasData(
            new ReleaseCategory { Id = 1, Code = "NORMAL_RELEASE", Name = "Normal release", RequiresClusterCirculation = true, RequiresInterview = true, RequiresCompetencyNotification = true, IsActive = true },
            new ReleaseCategory { Id = 2, Code = "RESIGNATION", Name = "Resignation", IsActive = true },
            new ReleaseCategory { Id = 3, Code = "PERFORMANCE", Name = "Performance issues", IsActive = true },
            new ReleaseCategory { Id = 4, Code = "MEDICAL", Name = "Medical reasons", IsActive = true },
            new ReleaseCategory { Id = 5, Code = "LONG_LEAVE", Name = "Long leave", IsActive = true });

        b.Entity<SlaConfiguration>().HasData(new SlaConfiguration { Id = 1 });
    }

    // History and audit rows are append-only. Refuse to save any change to an existing one.
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var tampered = ChangeTracker.Entries().Any(e =>
            (e.Entity is AuditLog || e.Entity is TicketStatusHistory) &&
            (e.State == EntityState.Modified || e.State == EntityState.Deleted));
        if (tampered) throw new InvalidOperationException("Audit and history records are append-only.");
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
