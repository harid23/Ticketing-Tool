using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using ReleaseTicketing.Core.Abstractions;
using ReleaseTicketing.Core.Configuration;
using ReleaseTicketing.Core.Data;
using ReleaseTicketing.Core.Domain;
using ReleaseTicketing.Core.Dtos;
using ReleaseTicketing.Core.Services;

namespace ReleaseTicketing.Tests;

public class TestUser : ICurrentUser
{
    public int? UserId { get; set; }
    public string DisplayName { get; set; } = "Tester";
    public string? Email { get; set; }
    public int? ClusterId { get; set; }
    public string? IpAddress => "127.0.0.1";
    public bool IsSystem { get; set; }
    public IReadOnlySet<string> Roles { get; set; } = new HashSet<string>();
}

public class FakeNumbers : ITicketNumberGenerator
{
    private static int _n;
    public Task<string> NextAsync(int year, CancellationToken ct) => Task.FromResult($"RR-{year}-{Interlocked.Increment(ref _n):D6}");
}

public class FakeBlobStore : IBlobStore
{
    public Dictionary<string, byte[]> Files { get; } = [];
    public async Task UploadAsync(string path, Stream content, string contentType, CancellationToken ct) { using var ms = new MemoryStream(); await content.CopyToAsync(ms, ct); Files[path] = ms.ToArray(); }
    public Task<Stream> OpenReadAsync(string path, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream(Files[path]));
}

/// <summary>A set of services wired to one DbContext, acting as one particular user (like one HTTP request).</summary>
public class Svc
{
    public ReleaseRequestService Release { get; }
    public EvaluationService Eval { get; }
    public ActivityFeedService Activity { get; }
    public AttachmentService Attach { get; }
    public SlaService Sla { get; }

    public Svc(AppDbContext db, TestUser user, FakeTimeProvider clock, FakeBlobStore blobs)
    {
        var audit = new AuditService(db, user, clock);
        var notify = new NotificationService(db, clock);
        var wf = new TicketWorkflow(db, audit, notify, user, clock);
        Release = new ReleaseRequestService(db, audit, notify, wf, new WorkingCalendarService(db), new FakeNumbers(), user, clock,
            Options.Create(new BusinessOptions { BusinessTimeZone = "UTC" }));
        Eval = new EvaluationService(db, audit, notify, wf, user, clock);
        Activity = new ActivityFeedService(db, user, clock);
        Attach = new AttachmentService(db, blobs, audit, user, clock, Options.Create(new AttachmentOptions()));
        Sla = new SlaService(db, audit, notify, wf, clock, NullLogger<SlaService>.Instance);
    }
}

public class Harness : IDisposable
{
    private readonly SqliteConnection _conn = new("DataSource=:memory:");
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));   // a Monday
    public FakeBlobStore Blobs { get; } = new();
    public Func<AppDbContext> DbFactory { get; }

    public int PmId = 101, ManagerEy2 = 202, ManagerEy3 = 203, ManagerEy4 = 204, HeadId = 301;
    public int Ey1 = 1, Ey2 = 2, Ey3 = 3, Ey4 = 4, Ey5 = 5;

    public Harness(Func<AppDbContext>? factory = null)
    {
        if (factory is null)
        {
            _conn.Open();
            DbFactory = () => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options);
        }
        else DbFactory = factory;

        using var db = DbFactory();
        if (factory is not null) db.Database.EnsureDeleted();   // SQL Server test DB only; in-memory SQLite starts empty
        db.Database.EnsureCreated();   // also applies the seed data (roles, clusters, categories, SLA)
        void U(int id, string name, string role, int? cluster) { db.Users.Add(new User { Id = id, EntraObjectId = $"oid-{id}", Email = $"{name}@x.com", DisplayName = name, ClusterId = cluster, CreatedAt = Clock.GetUtcNow().UtcDateTime });
            var roleId = role switch { RoleNames.Pm => 1, RoleNames.Asdm => 2, RoleNames.Admin => 7, _ => throw new InvalidOperationException($"Unknown test role '{role}'.") };
            db.UserRoles.Add(new UserRole { UserId = id, RoleId = roleId }); }
        U(PmId, "pm", RoleNames.Pm, Ey1); U(ManagerEy2, "mgr2", RoleNames.ClusterManager, Ey2); U(ManagerEy3, "mgr3", RoleNames.ClusterManager, Ey3);
        U(ManagerEy4, "mgr4", RoleNames.ClusterManager, Ey4); U(205, "mgr5", RoleNames.ClusterManager, Ey5); U(HeadId, "head", RoleNames.Head, null);
        db.SaveChanges();
        foreach (var c in db.Clusters) c.ManagerUserId = c.Id switch { 2 => ManagerEy2, 3 => ManagerEy3, 4 => ManagerEy4, _ => null };
        db.Employees.Add(new Employee { EmployeeId = "E100", Name = "Asha", Grade = "G5", Location = "Chennai", Supervisor = "Ravi", ProjectId = "P1", ProjectName = "Proj", Psl = "PSL1", Band = "B2", IsActive = true });
        db.SaveChanges();
    }

    public TestUser Pm() => new() { UserId = PmId, DisplayName = "pm", ClusterId = Ey1, Roles = new HashSet<string> { RoleNames.Pm } };
    public TestUser Head() => new() { UserId = HeadId, DisplayName = "head", Roles = new HashSet<string> { RoleNames.Head } };
    public TestUser Manager(int cluster) => new() { UserId = 200 + cluster, DisplayName = $"mgr{cluster}", ClusterId = cluster, Roles = new HashSet<string> { RoleNames.ClusterManager } };
    public TestUser System() => new() { IsSystem = true, DisplayName = "SYSTEM" };

    /// <summary>Fresh DbContext + services for this user (like one HTTP request).</summary>
    public Svc As(TestUser u) => new(DbFactory(), u, Clock, Blobs);

    public CreateReleaseRequestDto ValidDto(int categoryId = 1, string employeeId = "E100") => new()
    {
        EmployeeId = employeeId, EmployeeName = "Asha", ClusterId = Ey1, ReleaseCategoryId = categoryId, ReleaseDate = new DateOnly(2026, 11, 1),
        ReasonForRelease = "Project winding down", EmployeeSkills = "C#, SQL", FeedbackProvided = "Yes",
        DetailedFeedback = new string('x', 120), Grade = "G5", Location = "Chennai", CurrentSupervisor = "Ravi", CurrentProjectId = "P1",
        CurrentProjectName = "Proj", ReleaseLocation = "Chennai", Psl = "PSL1", Band = "B2"
    };

    public async Task<ReleaseRequestDetailDto> CreateTicketAsync(int categoryId = 1)
        => await As(Pm()).Release.CreateAsync(ValidDto(categoryId), default);

    public void Dispose() => _conn.Dispose();
}
