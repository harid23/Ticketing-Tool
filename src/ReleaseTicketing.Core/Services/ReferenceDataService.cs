using Microsoft.EntityFrameworkCore;
using ReleaseTicketing.Core.Abstractions;
using ReleaseTicketing.Core.Data;
using ReleaseTicketing.Core.Dtos;

namespace ReleaseTicketing.Core.Services;

public class ReferenceDataService(AppDbContext db, ICurrentUser user)
{
    public async Task<EmployeeDto?> GetEmployeeAsync(string employeeId, CancellationToken ct)
    {
        var id = (employeeId ?? "").Trim();
        return await db.Employees.AsNoTracking().Where(e => e.EmployeeId == id && e.IsActive)
            .Select(e => new EmployeeDto(e.EmployeeId, e.Name, e.Grade, e.Location, e.Supervisor, e.ProjectId, e.ProjectName, e.Psl, e.Band, e.Skills))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<ClusterDto>> GetClustersAsync(CancellationToken ct)
        => await db.Clusters.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Code).Select(c => new ClusterDto(c.Id, c.Code, c.Name)).ToListAsync(ct);

    public async Task<IReadOnlyList<ReleaseCategoryDto>> GetCategoriesAsync(CancellationToken ct)
        => await db.ReleaseCategories.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Id)
            .Select(c => new ReleaseCategoryDto(c.Id, c.Code, c.Name, c.RequiresClusterCirculation, c.RequiresInterview)).ToListAsync(ct);

    public MeDto Me() => new(user.UserId, user.DisplayName, user.Email, user.ClusterId, user.Roles);
}
