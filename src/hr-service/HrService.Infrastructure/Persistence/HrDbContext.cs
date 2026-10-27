using HrService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HrService.Infrastructure.Persistence;

public class HrDbContext(DbContextOptions<HrDbContext> options) : DbContext(options)
{
    // khai báo bảng (mỗi DbSet là một bảng trong database) 
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeavePolicy> LeavePolicies => Set<LeavePolicy>();
    public DbSet<Holiday> Holidays => Set<Holiday>();

    // cấu hình chi tiết từng bảng (độ dài, unique, khóa ngoại) 
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Department>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<Employee>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            e.Property(x => x.Email).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.PasswordHash).IsRequired();
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);

            e.HasOne(x => x.Department).WithMany(d => d.Employees)
             .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Manager).WithMany(m => m.Subordinates)
             .HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<LeaveType>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
        });
        b.Entity<LeavePolicy>(e =>
        {
            e.Property(x => x.DaysPerYear).HasPrecision(5, 1);
            e.HasIndex(x => new { x.LeaveTypeId, x.Year }).IsUnique();
            e.HasOne(x => x.LeaveType).WithMany(t => t.Policies)
             .HasForeignKey(x => x.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Holiday>(e =>
{
    e.HasIndex(x => x.HolidayDate).IsUnique();
    e.Property(x => x.Name).HasMaxLength(150).IsRequired();
});
    }
}