namespace HrService.Domain.Entities;

public class Holiday
{
    public int Id { get; set; }
    public DateOnly HolidayDate { get; set; }
    public string Name { get; set; } = string.Empty;
}