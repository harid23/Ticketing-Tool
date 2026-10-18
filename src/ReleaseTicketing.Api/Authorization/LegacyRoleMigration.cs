using Microsoft.EntityFrameworkCore;
using ReleaseTicketing.Core.Data;
using ReleaseTicketing.Core.Domain;

namespace ReleaseTicketing.Api.Authorization;

public static class LegacyRoleMigration
{
    public static async Task ApplyAsync(AppDbContext db, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var roles = await db.Roles.ToListAsync(ct);

        foreach (var target in RoleNames.All)
        {
            if (roles.Any(role => role.Name == target)) continue;

            var legacy = roles.FirstOrDefault(role => RoleNames.Normalize(role.Name) == target);
            if (legacy is null)
            {
                legacy = new Role { Id = roles.Select(role => role.Id).DefaultIfEmpty().Max() + 1 };
                db.Roles.Add(legacy);
                roles.Add(legacy);
            }
            legacy.Name = target;
            legacy.Description = target;
            await db.SaveChangesAsync(ct);
        }

        roles = await db.Roles.ToListAsync(ct);
        var rolesById = roles.ToDictionary(role => role.Id);
        var memberships = await db.UserRoles.ToListAsync(ct);
        var desiredMemberships = memberships
            .Where(membership => rolesById.ContainsKey(membership.RoleId))
            .Select(membership => (membership.UserId, Role: RoleNames.Normalize(rolesById[membership.RoleId].Name)))
            .Where(membership => membership.Role is not null)
            .Select(membership => (membership.UserId, RoleId: roles.Single(role => role.Name == membership.Role).Id))
            .Distinct()
            .ToList();

        var legacyRoleIds = roles.Where(role => !RoleNames.All.Contains(role.Name))
            .Select(role => role.Id).ToHashSet();
        if (legacyRoleIds.Count > 0)
        {
            db.UserRoles.RemoveRange(memberships.Where(membership => legacyRoleIds.Contains(membership.RoleId)));
            await db.SaveChangesAsync(ct);
        }

        var remainingMemberships = await db.UserRoles.Select(membership => new { membership.UserId, membership.RoleId }).ToListAsync(ct);
        foreach (var membership in desiredMemberships)
        {
            if (remainingMemberships.Any(existing => existing.UserId == membership.UserId && existing.RoleId == membership.RoleId))
                continue;
            db.UserRoles.Add(new UserRole { UserId = membership.UserId, RoleId = membership.RoleId });
        }
        await db.SaveChangesAsync(ct);

        var legacyRoles = await db.Roles
            .Where(role => !RoleNames.All.Contains(role.Name) && role.Name != RoleNames.Pm)
            .ToListAsync(ct);
        db.Roles.RemoveRange(legacyRoles);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
