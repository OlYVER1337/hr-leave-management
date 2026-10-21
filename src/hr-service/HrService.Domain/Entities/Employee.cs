namespace HrService.Domain.Entities;
public class Employee
{
    public Guid Id { get ; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash{ get; set; } = string.Empty;
    public Role Role { get; set; } = Role.Employee;
    public DateOnly HireDate { get; set; }
    public bool IsActive { get; set; } =true;
    public int DepartmentId { get; set; }
    public Department? Department {get; set; }

    public Guid? ManagerId { get; set; }

    public Employee? Manager{ get; set; }
    public ICollection<Employee> Subordinates { get; set; } = new List<Employee>();

}