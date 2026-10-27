using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LeaveService.Infrastructure.Persistence;

public sealed class LeaveDbContextFactory : IDesignTimeDbContextFactory<LeaveDbContext>
{
    public LeaveDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("LEAVE_DESIGN_CONNECTION_STRING")
            ?? "Server=localhost;Database=Leave_DB;Integrated Security=True;TrustServerCertificate=True;Encrypt=False";
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new LeaveDbContext(options);
    }
}
