using System.ComponentModel.DataAnnotations;
namespace HrService.Api.Dtos;

public class UpdateDepartmentRequest
{
    [Required]
    [StringLength(100, MinimumLength =2)]
    public String Name { get; set; } = string.Empty;
}