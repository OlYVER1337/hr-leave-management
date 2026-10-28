using LeaveService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LeaveService.Infrastructure.Persistence;

public sealed class LeaveDbContext(DbContextOptions<LeaveDbContext> options) : DbContext(options)
{
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<LeaveBalance> LeaveBalances => Set<LeaveBalance>();
    public DbSet<LeaveRequestHistory> LeaveRequestHistories => Set<LeaveRequestHistory>();
    public DbSet<LeaveRequestYearAllocation> LeaveRequestYearAllocations => Set<LeaveRequestYearAllocation>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<LeaveBalance>(entity =>
        {
            entity.HasIndex(item => new { item.EmployeeId, item.LeaveTypeId, item.Year })
                .IsUnique();
            entity.Property(item => item.Entitled).HasPrecision(6, 1);
            entity.Property(item => item.Used).HasPrecision(6, 1);
            entity.Property(item => item.Pending).HasPrecision(6, 1);
            entity.Property(item => item.RowVersion).IsRowVersion();
        });

        builder.Entity<LeaveRequest>(entity =>
        {
            entity.HasIndex(item => new { item.EmployeeId, item.Status, item.StartDate, item.EndDate });
            entity.Property(item => item.TotalDays).HasPrecision(6, 1);
            entity.Property(item => item.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(item => item.RowVersion).IsRowVersion();
        });

        builder.Entity<LeaveRequestHistory>(entity =>
        {
            entity.HasIndex(item => new { item.LeaveRequestId, item.CreatedAtUtc });
            entity.Property(item => item.Action).HasMaxLength(30).IsRequired();
            entity.Property(item => item.FromStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(item => item.ToStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(item => item.Note).HasMaxLength(1000);
            entity.HasOne(item => item.LeaveRequest)
                .WithMany(item => item.Histories)
                .HasForeignKey(item => item.LeaveRequestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<LeaveRequestYearAllocation>(entity =>
        {
            entity.HasKey(item => new { item.LeaveRequestId, item.Year });
            entity.Property(item => item.Days).HasPrecision(6, 1);
            entity.HasOne(item => item.LeaveRequest)
                .WithMany(item => item.YearAllocations)
                .HasForeignKey(item => item.LeaveRequestId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
