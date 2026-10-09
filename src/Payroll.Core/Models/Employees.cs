using System.ComponentModel.DataAnnotations;
namespace Payroll.Core.Models;

public sealed record EmployeeListItem(int EmployeeId, string EmployeeCode, string FullName, string Email, string Department, string Designation, DateOnly JoinDate, bool IsActive);

public class EmployeeInput
{
    public int EmployeeId { get; set; }
    [Required, StringLength(60), Display(Name = "First name")] public string FirstName { get; set; } = "";
    [Required, StringLength(60), Display(Name = "Last name")] public string LastName { get; set; } = "";
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [StringLength(30)] public string? Phone { get; set; }
    [Required, Display(Name = "Date of birth")] public DateOnly? DateOfBirth { get; set; }
    [Required, Display(Name = "Join date")] public DateOnly? JoinDate { get; set; }
    [Required, Display(Name = "Department")] public int DepartmentId { get; set; }
    [Required, Display(Name = "Designation")] public int DesignationId { get; set; }
    [Display(Name = "Manager")] public int? ManagerId { get; set; }
    [Required, Display(Name = "Employment type")] public string EmploymentType { get; set; } = "FullTime";
    [StringLength(30), Display(Name = "Tax ID")] public string? TaxIdNumber { get; set; }
    [StringLength(100), Display(Name = "Bank")] public string? BankName { get; set; }
    [StringLength(40), Display(Name = "Account number")] public string? BankAccountNumber { get; set; }
    public byte[]? RowVersion { get; set; }
}

public sealed class EmployeeDetail
{
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string FullName => $"{FirstName} {LastName}";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public DateOnly DateOfBirth { get; set; }
    public DateOnly JoinDate { get; set; }
    public DateOnly? ExitDate { get; set; }
    public int DepartmentId { get; set; }
    public string Department { get; set; } = "";
    public int DesignationId { get; set; }
    public string Designation { get; set; } = "";
    public int? ManagerId { get; set; }
    public string? ManagerName { get; set; }
    public string EmploymentType { get; set; } = "";
    public string? TaxIdNumber { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountNumber { get; set; }
    public bool IsActive { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
