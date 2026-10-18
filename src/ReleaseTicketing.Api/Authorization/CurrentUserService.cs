using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ReleaseTicketing.Core.Abstractions;
using ReleaseTicketing.Core.Data;
using ReleaseTicketing.Core.Domain;

namespace ReleaseTicketing.Api.Authorization;

/// <summary>
/// Implements ICurrentUser for web requests. On the first call after sign-in it creates the user row, and it keeps
/// the user_roles table in line with the App Roles in the token (the token is the source of truth for roles).
/// </summary>
public class CurrentUserService(AppDbContext db, TimeProvider clock) : ICurrentUser
{
    public int? UserId { get; private set; }
    public string DisplayName { get; private set; } = "anonymous";
    public string? Email { get; private set; }
    public int? ClusterId { get; private set; }
    public string? IpAddress { get; private set; }
    public bool IsSystem => false;
    public IReadOnlySet<string> Roles { get; private set; } = new HashSet<string>();

    public async Task InitializeAsync(ClaimsPrincipal principal, string? ip, CancellationToken ct)
    {
        IpAddress = ip;
        var oid = principal.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value ?? principal.FindFirst("oid")?.Value ?? "local-dev-user";

        var isLocalDevPrincipal = principal.Identity is null || !principal.Identity.IsAuthenticated || principal.Claims.Count() == 0 || string.Equals(principal.Identity.AuthenticationType, "LocalDevelopment", StringComparison.OrdinalIgnoreCase);
        var email = principal.FindFirst("preferred_username")?.Value ?? principal.FindFirst(ClaimTypes.Email)?.Value ?? principal.FindFirst("email")?.Value ?? "local-dev@localhost";
        var name = principal.FindFirst("name")?.Value ?? principal.FindFirst(ClaimTypes.Name)?.Value ?? email;
        if (isLocalDevPrincipal && principal.Claims.Count() == 0)
        {
            email = "local-dev@localhost";
            name = "local-dev-user";
        }
        else if (isLocalDevPrincipal && string.IsNullOrWhiteSpace(name))
        {
            name = "local-dev-user";
        }
        var tokenRoles = principal.FindAll("roles").Concat(principal.FindAll(ClaimTypes.Role))
            .Select(c => RoleNames.Normalize(c.Value)).OfType<string>().ToHashSet();
        if (tokenRoles.Count == 0 && string.Equals(principal.Identity?.AuthenticationType, "LocalDevelopment", StringComparison.OrdinalIgnoreCase))
            tokenRoles.Add(RoleNames.Pm);
        if (tokenRoles.Count == 0 && string.IsNullOrEmpty(principal.Identity?.Name) && principal.Claims.Count() == 0)
            tokenRoles.Add(RoleNames.Pm);

        var user = await db.Users.Include(u => u.UserRoles).FirstOrDefaultAsync(u => u.EntraObjectId == oid, ct);
        int? localClusterId = null;
        if (string.Equals(principal.Identity?.AuthenticationType, "LocalDevelopment", StringComparison.OrdinalIgnoreCase))
        {
            var clusterClaim = principal.FindFirst("local_cluster_id")?.Value;
            if (!string.IsNullOrWhiteSpace(clusterClaim))
            {
                if (!int.TryParse(clusterClaim, out var parsedClusterId) ||
                    !await db.Clusters.AnyAsync(c => c.Id == parsedClusterId && c.IsActive, ct))
                    throw new ReleaseTicketing.Core.Exceptions.ForbiddenException("Configured local development cluster is invalid or inactive.");
                localClusterId = parsedClusterId;
            }
        }

        if (user is null)
        {
            user = new User { EntraObjectId = oid, Email = email, DisplayName = name, ClusterId = localClusterId, CreatedAt = clock.GetUtcNow().UtcDateTime };
            db.Users.Add(user);
        }
        else
        {
            user.Email = email;
            user.DisplayName = name;
            if (string.Equals(principal.Identity?.AuthenticationType, "LocalDevelopment", StringComparison.OrdinalIgnoreCase))
                user.ClusterId = localClusterId;
        }

        var roleRows = await db.Roles.ToListAsync(ct);
        var roleRowsById = roleRows.ToDictionary(r => r.Id, r => r);

        foreach (var r in roleRows.Where(r => tokenRoles.Contains(r.Name) && user.UserRoles.All(ur => ur.RoleId != r.Id)))
            user.UserRoles.Add(new UserRole { Role = r });

        foreach (var ur in user.UserRoles.Where(ur =>
                 {
                     var assignedRole = ur.Role ?? roleRowsById.GetValueOrDefault(ur.RoleId);
                     return assignedRole is null || !tokenRoles.Contains(assignedRole.Name);
                 }).ToList())
            user.UserRoles.Remove(ur);

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            user = await db.Users.AsNoTracking().FirstAsync(u => u.EntraObjectId == oid, ct);
        }

        UserId = user.Id; DisplayName = user.DisplayName; Email = user.Email; ClusterId = user.ClusterId; Roles = tokenRoles;
    }
}
