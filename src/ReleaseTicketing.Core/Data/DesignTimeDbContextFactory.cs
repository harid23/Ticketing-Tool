using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ReleaseTicketing.Core.Data;

/// <summary>Lets "dotnet ef" create the DbContext without starting the web app. Set RT_CONNECTION first.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("RT_CONNECTION")
                 ?? "Server=(localdb)\\MSSQLLocalDB;Database=ReleaseTicketing;Trusted_Connection=True;TrustServerCertificate=True";
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs).Options);
    }
}
